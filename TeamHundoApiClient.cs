using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace card_overview_wpf
{
    public class TeamHundoApiClient : IDisposable
    {
        private const int FirehoseReconnectDelayMilliseconds = 2000;
        private const string DefaultBaseApiUrl = "https://hundo.maika.moe";

        private readonly string baseApiUrl;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private CancellationTokenSource firehoseCancellationTokenSource;
        private Task firehoseTask;
        private ClientWebSocket firehoseSocket;
        private readonly object firehoseLock = new object();

        static TeamHundoApiClient()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        }

        public TeamHundoApiClient(string baseApiUrl)
        {
            if (string.IsNullOrWhiteSpace(baseApiUrl))
            {
                throw new ArgumentException("A base API URL is required.", "baseApiUrl");
            }

            this.baseApiUrl = baseApiUrl.TrimEnd('/');
        }

        public static TeamHundoApiClient FromConfiguration()
        {
            return new TeamHundoApiClient(GetConfiguredBaseApiUrl());
        }

        private static string GetConfiguredBaseApiUrl()
        {
            string[] commandLineArgs = Environment.GetCommandLineArgs();
            if (commandLineArgs.Length > 1 && !string.IsNullOrWhiteSpace(commandLineArgs[1]))
            {
                return commandLineArgs[1];
            }

            string configuredBaseApiUrl = ConfigurationManager.AppSettings["TeamHundoBaseApiUrl"];
            if (!string.IsNullOrWhiteSpace(configuredBaseApiUrl))
            {
                return configuredBaseApiUrl;
            }

            return DefaultBaseApiUrl;
        }

        public IList<TeamJson> GetTeams()
        {
            List<TeamJson> teams = GetJson<List<TeamJson>>("/api/teams");
            return teams ?? new List<TeamJson>();
        }

        public IList<int> GetLibraryContents(int teamId)
        {
            List<int> cardIds = GetJson<List<int>>("/api/library_contents/" + teamId);
            return cardIds ?? new List<int>();
        }

        public LibraryUpdate GetLibrary(int teamId)
        {
            return GetJson<LibraryUpdate>("/api/library/" + teamId);
        }

        private T GetJson<T>(string endpointPath)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(baseApiUrl + endpointPath);
            request.Method = "GET";
            request.Accept = "application/json";

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string json = reader.ReadToEnd();
                    return serializer.Deserialize<T>(json);
                }
            }
            catch (WebException ex)
            {
                throw new InvalidOperationException(BuildApiErrorMessage(ex), ex);
            }
        }

        public void StartTeamFirehose(Action<LibraryUpdate> updateReceived)
        {
            if (updateReceived == null)
            {
                throw new ArgumentNullException("updateReceived");
            }

            StopTeamFirehose();

            lock (firehoseLock)
            {
                firehoseCancellationTokenSource = new CancellationTokenSource();
                firehoseTask = Task.Run(() => RunTeamFirehoseLoop(updateReceived, firehoseCancellationTokenSource.Token));
            }
        }

        public void StopTeamFirehose()
        {
            CancellationTokenSource cancellationTokenSource;
            Task runningTask;
            ClientWebSocket socket;

            lock (firehoseLock)
            {
                cancellationTokenSource = firehoseCancellationTokenSource;
                runningTask = firehoseTask;
                socket = firehoseSocket;

                firehoseCancellationTokenSource = null;
                firehoseTask = null;
                firehoseSocket = null;
            }

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
            }

            if (socket != null)
            {
                socket.Abort();
                socket.Dispose();
            }

            if (runningTask != null)
            {
                try
                {
                    runningTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                }
            }

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Dispose();
            }
        }

        public void Dispose()
        {
            StopTeamFirehose();
        }

        private async Task RunTeamFirehoseLoop(Action<LibraryUpdate> updateReceived, CancellationToken cancellationToken)
        {
            Uri firehoseUri = new Uri(BuildTeamFirehoseUrl());

            while (!cancellationToken.IsCancellationRequested)
            {
                ClientWebSocket socket = null;

                try
                {
                    socket = new ClientWebSocket();

                    lock (firehoseLock)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            socket.Dispose();
                            break;
                        }

                        firehoseSocket = socket;
                    }

                    await socket.ConnectAsync(firehoseUri, cancellationToken).ConfigureAwait(false);

                    while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                    {
                        string message = await ReceiveTextMessage(socket, cancellationToken).ConfigureAwait(false);
                        if (message == null)
                        {
                            break;
                        }

                        HandleFirehoseMessage(message, updateReceived);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await DelayBeforeReconnect(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Team firehose connection failed: " + ex.Message);
                    await DelayBeforeReconnect(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    lock (firehoseLock)
                    {
                        if (firehoseSocket == socket)
                        {
                            firehoseSocket = null;
                        }
                    }

                    if (socket != null)
                    {
                        socket.Abort();
                        socket.Dispose();
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    await DelayBeforeReconnect(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static async Task<string> ReceiveTextMessage(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];

            using (MemoryStream messageStream = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return null;
                    }

                    if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                    {
                        messageStream.Write(buffer, 0, result.Count);
                    }
                }
                while (!result.EndOfMessage);

                return Encoding.UTF8.GetString(messageStream.ToArray());
            }
        }

        private void HandleFirehoseMessage(string message, Action<LibraryUpdate> updateReceived)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                LibraryUpdate update = serializer.Deserialize<LibraryUpdate>(message);
                if (update != null)
                {
                    updateReceived(update);
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("Unable to deserialize team firehose update: " + ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine("Unable to deserialize team firehose update: " + ex.Message);
            }
        }

        private static async Task DelayBeforeReconnect(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(FirehoseReconnectDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
            }
        }

        private string BuildTeamFirehoseUrl()
        {
            Uri baseUri = new Uri(baseApiUrl);
            UriBuilder builder = new UriBuilder(baseUri);
            if (string.Equals(builder.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                builder.Scheme = "wss";
            }
            else if (string.Equals(builder.Scheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                builder.Scheme = "ws";
            }

            builder.Path = CombineUrlPaths(builder.Path, "/firehose/team");
            builder.Query = string.Empty;
            builder.Fragment = string.Empty;
            return builder.Uri.ToString();
        }

        private static string CombineUrlPaths(string basePath, string endpointPath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
            {
                return endpointPath;
            }

            return basePath.TrimEnd('/') + endpointPath;
        }

        private static string BuildApiErrorMessage(WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response == null)
            {
                return ex.Message;
            }

            string responseBody = string.Empty;
            using (Stream responseStream = response.GetResponseStream())
            {
                if (responseStream != null)
                {
                    using (StreamReader reader = new StreamReader(responseStream))
                    {
                        responseBody = reader.ReadToEnd();
                    }
                }
            }

            ApiErrorJson error = null;
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                try
                {
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    error = serializer.Deserialize<ApiErrorJson>(responseBody);
                }
                catch (ArgumentException)
                {
                    error = null;
                }
                catch (InvalidOperationException)
                {
                    error = null;
                }
            }

            string detail = null;
            if (error != null)
            {
                detail = !string.IsNullOrWhiteSpace(error.message) ? error.message : error.error;
            }

            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = responseBody;
            }
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = response.StatusDescription;
            }

            return "Team API request failed with HTTP " + (int)response.StatusCode + " " + response.StatusCode + ": " + detail;
        }
    }

    public class TeamJson
    {
        public int id { get; set; }
        public string name { get; set; }
    }

    public class LibraryUpdate
    {
        public int teamId { get; set; }
        public int bewdCount { get; set; }
        public List<CardAcquisition> newAcquisitions { get; set; }
    }

    public class CardAcquisition
    {
        public int cardId { get; set; }
    }

    public class ApiErrorJson
    {
        public string message { get; set; }
        public string error { get; set; }
    }
}

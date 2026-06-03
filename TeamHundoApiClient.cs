using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WebSocket4Net;

namespace card_overview_wpf
{
    public class TeamHundoApiClient : IDisposable
    {
        private const int FirehoseReconnectDelayMilliseconds = 2000;

        private readonly string baseApiUrl;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private CancellationTokenSource firehoseCancellationTokenSource;
        private Task firehoseTask;
        private WebSocket firehoseSocket;
        private readonly object firehoseLock = new object();

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
            return new TeamHundoApiClient(ConfigurationManager.AppSettings["TeamHundoBaseApiUrl"]);
        }

        public IList<TeamJson> GetTeams()
        {
            List<TeamJson> teams = GetJson<List<TeamJson>>("/api/teams");
            return teams ?? new List<TeamJson>();
        }

        public IList<CardAcquisition> GetLibraryContents(int teamId)
        {
            List<CardAcquisition> cardAcquisitions = GetJson<List<CardAcquisition>>("/api/library_contents/" + teamId);
            return cardAcquisitions ?? new List<CardAcquisition>();
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
            WebSocket socket;

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
                socket.Close();
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
            string firehoseUrl = BuildTeamFirehoseUrl();

            while (!cancellationToken.IsCancellationRequested)
            {
                WebSocket socket = null;
                TaskCompletionSource<bool> disconnected = new TaskCompletionSource<bool>();

                try
                {
                    socket = new WebSocket(firehoseUrl);
                    socket.MessageReceived += (sender, e) => HandleFirehoseMessage(e.Message, updateReceived);
                    socket.Closed += (sender, e) => disconnected.TrySetResult(true);
                    socket.Error += (sender, e) => disconnected.TrySetResult(true);

                    lock (firehoseLock)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            socket.Close();
                            break;
                        }

                        firehoseSocket = socket;
                    }

                    socket.Open();
                    await disconnected.Task.ConfigureAwait(false);
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
                        socket.Close();
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    await DelayBeforeReconnect(cancellationToken).ConfigureAwait(false);
                }
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
                detail = !string.IsNullOrWhiteSpace(error.Message) ? error.Message : error.Error;
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
        public int TeamId { get; set; }
        public int Id { get; set; }
        public string Name { get; set; }

        public int SelectedTeamId
        {
            get { return TeamId != 0 ? TeamId : Id; }
        }
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
        public string Message { get; set; }
        public string Error { get; set; }
    }
}

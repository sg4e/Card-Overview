using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Web.Script.Serialization;

namespace card_overview_wpf
{
    public class TeamHundoApiClient
    {
        private readonly string baseApiUrl;

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
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(baseApiUrl + "/api/teams");
            request.Method = "GET";
            request.Accept = "application/json";

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string json = reader.ReadToEnd();
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    List<TeamJson> teams = serializer.Deserialize<List<TeamJson>>(json);
                    return teams ?? new List<TeamJson>();
                }
            }
            catch (WebException ex)
            {
                throw new InvalidOperationException(BuildApiErrorMessage(ex), ex);
            }
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

    public class ApiErrorJson
    {
        public string Message { get; set; }
        public string Error { get; set; }
    }
}

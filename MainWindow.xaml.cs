using CsvHelper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace card_overview_wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DataTable cardTable;

        //Windows
        private CardWindow cardWindow;
        private SearchBox searchBox;
        private Settings settings;
        private About about;

        private TeamHundoApiClient teamHundoApiClient;
        private IList<TeamJson> connectedTeams = new List<TeamJson>();

        private List<List<CardView>> cards;
        private HashSet<int> ownedCardIds = new HashSet<int>();
        private int bewdCount = 0;
        private int cols = 1;
        private int rows = 7;

        //Settings
        private int cardWidth = 100;
        private int cardHeight = 100;
        private string iconLocation = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\icons\\";
        private string backgroundColor = "#00FF00";
        private Color tbBackgroundColor = Colors.White;
        private Color tbTextColor = Colors.Black;
        private int? selectedTeamId;
        private string selectedTeamName;

        public MainWindow()
        {
            InitializeComponent();
        }



        public int? SelectedTeamId
        {
            get { return selectedTeamId; }
        }



        public int GetCardId(string name)
        {
            DataRow result = cardTable.Select("name = '" + name + "'")[0];
            int id = int.Parse(result["id"].ToString());
            return id;
        }

        public string GetCardFilename(int cardid)
        {
            DataRow result = cardTable.Select("id = '" + cardid + "'")[0];
            int id = int.Parse(result["id"].ToString());
            return id.ToString("D3") + ".PNG";
        }

        private void LoadCardList()
        {
            //Setup the table
            cardTable = new DataTable();

            cardTable.Columns.Add("id", typeof(int));
            cardTable.Columns.Add("name", typeof(string));
            cardTable.Columns.Add("cardtype", typeof(string));
            cardTable.Columns.Add("type", typeof(string));
            cardTable.CaseSensitive = false;

            string filename = "fm-cards.csv";
            CsvReader csv = new CsvReader(File.OpenText(filename));
            csv.Configuration.WillThrowOnMissingField = false;

            while (csv.Read())
            {
                DataRow row = cardTable.NewRow();
                foreach (DataColumn column in cardTable.Columns)
                {
                    row[column.ColumnName] = csv.GetField(column.DataType, column.ColumnName);
                }
                cardTable.Rows.Add(row);
            }
        }

        private void Resize()
        {
            cardWindow.Width = (cols * cardWidth) + 17;
            cardWindow.Height = (rows * cardHeight) + 40;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e) //Open
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Card Overview Files (*.cof)|*.cof";
            openFileDialog.RestoreDirectory = true;

            if (openFileDialog.ShowDialog() == true)
            {
                if (openFileDialog.FileName != "")
                {
                    LoadFromFile(openFileDialog.FileName);
                }
            }
        }

        private bool LoadFromFile(string filename)
        {
            Stream stream;
            if ((stream = File.Open(filename, FileMode.Open)) != null)
            {
                //Clear everything
                cardWindow.ClearAll();
                ClearAll();

                cards = new List<List<CardView>>();

                using (BinaryReader reader = new BinaryReader(stream))
                {
                    cols = reader.ReadInt32();
                    rows = reader.ReadInt32();



                    for (int i = 0; i < cols; i++)
                    {
                        List<CardView> colC = new List<CardView>();

                        for (int j = 0; j < rows; j++)
                        {
                            CardView cv = new CardView(this);
                            cv.SetTbBackgroundColor(tbBackgroundColor);
                            cv.SetTextColor(tbTextColor);
                            SetCardImage(cv, reader.ReadInt32());
                            cv.SetVisibility(reader.ReadBoolean());
                            colC.Add(cv);

                            Canvas.SetLeft(cv, cardWidth * i);
                            Canvas.SetTop(cv, cardHeight * j);
                            cardWindow.AddCardView(cv);
                        }

                        cards.Add(colC);
                    }

                    Resize();
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        private void ClearAll()
        {
        }

        private void MenuItem_Click_1(object sender, RoutedEventArgs e) //Save
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Card Overview Files (*.cof)|*.cof";
            saveFileDialog.RestoreDirectory = true;

            Stream stream;

            if (saveFileDialog.ShowDialog() == true)
            {
                if ((stream = saveFileDialog.OpenFile()) != null)
                {
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write(cols);
                        writer.Write(rows);
                        for (int i = 0; i < cols; i++)
                        {
                            for (int j = 0; j < rows; j++)
                            {
                                writer.Write(cards[i][j].GetCardId());
                                writer.Write((bool)(cards[i][j].GetVisibility()));
                            }
                        }
                    }
                }
            }
        }

        private void MenuItem_Click_2(object sender, RoutedEventArgs e) //Add Column
        {
            cols++;
            Resize();

            List<CardView> colC = new List<CardView>();

            int i = cols - 1;
            for (int j = 0; j < rows; j++)
            {
                CardView cv = new CardView(this);
                cv.SetTbBackgroundColor(tbBackgroundColor);
                cv.SetTextColor(tbTextColor);
                colC.Add(cv);

                Canvas.SetLeft(cv, cardWidth * i);
                Canvas.SetTop(cv, cardHeight * j);
                cardWindow.AddCardView(cv);
            }

            cards.Add(colC);
        }

        private void MenuItem_Click_4(object sender, RoutedEventArgs e) //Remove Column
        {
            //Remove column from both canvases
            int i = cols - 1;
            for (int j = 0; j < rows; j++)
            {
                cardWindow.RemoveCardView(cards[i][j]);
            }

            //Remove column from both lists
            cards.RemoveAt(cols - 1);

            //Resize
            cols--;
            Resize();
        }

        private void MenuItem_Click_3(object sender, RoutedEventArgs e) // Add Row
        {
            rows++;
            Resize();

            int j = rows - 1;
            for (int i = 0; i < cols; i++)
            {
                CardView cv = new CardView(this);
                cv.SetTbBackgroundColor(tbBackgroundColor);
                cv.SetTextColor(tbTextColor);
                cards[i].Add(cv);

                Canvas.SetLeft(cv, cardWidth * i);
                Canvas.SetTop(cv, cardHeight * j);
                cardWindow.AddCardView(cv);
            }
        }

        private void MenuItem_Click_5(object sender, RoutedEventArgs e) // Remove Row
        {
            //Remove row from both canvases
            int j = rows - 1;
            for (int i = 0; i < cols; i++)
            {
                cardWindow.RemoveCardView(cards[i][j]);

                //Remove row from card list
                cards[i].RemoveAt(j);
            }

            //Resize
            rows--;
            Resize();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (teamHundoApiClient != null)
            {
                teamHundoApiClient.Dispose();
                teamHundoApiClient = null;
            }

            if (cardWindow != null)
            {
                cardWindow.Close();
            }
            if (searchBox != null)
            {
                searchBox.Close();
            }
            if (settings != null)
            {
                settings.Close();
            }
            if (about != null)
            {
                about.Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            Application.Current.Shutdown();
        }

        public DataRow[] SearchCardList(string text)
        {
            DataRow[] result = new DataRow[0];

            int a;
            if (int.TryParse(text, out a))
            {
                DataRow[] result1 = cardTable.Select("id = " + a);
                result = result.Union(result1).ToArray();
            }

            DataRow[] result2 = cardTable.Select("name LIKE '*" + text + "*'");
            result = result.Union(result2).ToArray();

            DataRow[] result3 = cardTable.Select("cardtype LIKE '*" + text + "*'");
            result = result.Union(result3).ToArray();

            DataRow[] result4 = cardTable.Select("type LIKE '*" + text + "*'");
            result = result.Union(result4).ToArray();

            return result;
        }

        public void ShowSearchBox(CardView cv)
        {
            bool found = false;
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is SearchBox)
                    found = true;
            }

            if (!found)
            {
                searchBox = new SearchBox(this, cv);
                searchBox.Show();
            }
        }

        private void MenuItem_Click_6(object sender, RoutedEventArgs e) //Settings
        {
            bool found = false;
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is Settings)
                    found = true;
            }

            if (!found)
            {
                settings = new Settings();
                settings.Show(this);
            }
        }

        public void SetIconLocation(string location)
        {
            iconLocation = location;
        }

        public void SetBackgroundColor(string colorCode)
        {
            backgroundColor = colorCode;
            cardWindow.SetBackgroundColor(colorCode);
        }

        public void SetTbBackgroundColor(Color color)
        {
            tbBackgroundColor = color;
            foreach (List<CardView> column in cards)
            {
                foreach (CardView cv in column)
                {
                    cv.SetTbBackgroundColor(color);
                }
            }
        }

        public void SetTbTextColor(Color color)
        {
            tbTextColor = color;
            foreach (List<CardView> column in cards)
            {
                foreach (CardView cv in column)
                {
                    cv.SetTextColor(color);
                }
            }
        }

        public string GetTrackingValue(int cardId)
        {
            if (cardId == 1)
            {
                return bewdCount.ToString();
            }

            if (ownedCardIds.Contains(cardId))
            {
                return "✓";
            }

            return "0";
        }

        public void RefreshCardViews(int cardId)
        {
            if (cards == null)
            {
                return;
            }

            foreach (List<CardView> column in cards)
            {
                foreach (CardView cv in column)
                {
                    if (cv.GetCardId() == cardId)
                    {
                        cv.RefreshTrackingValue();
                    }
                }
            }
        }

        private void RefreshAllCardViews()
        {
            if (cards == null)
            {
                return;
            }

            foreach (List<CardView> column in cards)
            {
                foreach (CardView cv in column)
                {
                    cv.RefreshTrackingValue();
                }
            }
        }

        public void SetCardImage(CardView cardView, int cardId)
        {
            cardView.SetImage(cardId);
            cardView.RefreshTrackingValue();
        }

        public string GetIconLocation()
        {
            return iconLocation;
        }

        public string GetBackgroundColor()
        {
            return backgroundColor;
        }

        public Color GetTbBackgroundColor()
        {
            return tbBackgroundColor;
        }

        public Color GetTbTextColor()
        {
            return tbTextColor;
        }

        public void SaveSettings()
        {
            string filename = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\settings.dat";
            Stream stream;
            try
            {
                if (!File.Exists(filename))
                {
                    stream = File.Open(filename, FileMode.Create);
                    if (stream != null)
                    {
                        using (BinaryWriter writer = new BinaryWriter(stream))
                        {
                            writer.Write(iconLocation);
                            writer.Write(Array.IndexOf(Settings.colors, tbBackgroundColor));
                            writer.Write(Array.IndexOf(Settings.colors, tbTextColor));
                            writer.Write(backgroundColor);
                        }
                    }
                }
                else
                {
                    stream = File.Open(filename, FileMode.Open);
                    if (stream != null)
                    {
                        using (BinaryWriter writer = new BinaryWriter(stream))
                        {
                            writer.Write(iconLocation);
                            writer.Write(Array.IndexOf(Settings.colors, tbBackgroundColor));
                            writer.Write(Array.IndexOf(Settings.colors, tbTextColor));
                            writer.Write(backgroundColor);
                        }
                    }
                }
            }
            catch (System.IO.IOException e)
            {
                Console.WriteLine(e.StackTrace);
            }
        }

        public void LoadSettings()
        {
            string filename = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\settings.dat";
            if (File.Exists(filename))
            {
                Stream stream;
                if ((stream = File.Open(filename, FileMode.Open, FileAccess.Read)) != null)
                {
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        iconLocation = reader.ReadString();
                        tbBackgroundColor = Settings.colors[reader.ReadInt32()];
                        tbTextColor = Settings.colors[reader.ReadInt32()];
                        backgroundColor = reader.ReadString();
                    }
                }
            }
            else
            {
                SaveSettings();
            }
        }

        private void MenuItem_Click_7(object sender, RoutedEventArgs e) //About
        {
            bool found = false;
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is About)
                    found = true;
            }

            if (!found)
            {
                about = new About();
                about.Show();
            }
        }


        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            await ConnectToTeamHundoAsync();
        }

        private void TeamListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            setTeamButton.IsEnabled = teamListBox.SelectedItem is TeamJson && !selectedTeamId.HasValue;
        }

        private async void SetTeamButton_Click(object sender, RoutedEventArgs e)
        {
            TeamJson selectedTeam = teamListBox.SelectedItem as TeamJson;
            if (selectedTeam == null)
            {
                SetTeamStatus("Choose a team before setting auto-tracking.");
                return;
            }

            await SetSelectedTeamAsync(selectedTeam);
        }

        private async Task ConnectToTeamHundoAsync()
        {
            string baseApiUrl = teamUrlTextBox.Text;
            connectButton.IsEnabled = false;
            setTeamButton.IsEnabled = false;
            teamListBox.ItemsSource = null;
            connectedTeams = new List<TeamJson>();
            selectedTeamId = null;
            selectedTeamName = null;
            SetTeamStatus("Connecting to Team Hundo...");

            if (teamHundoApiClient != null)
            {
                teamHundoApiClient.Dispose();
                teamHundoApiClient = null;
            }

            try
            {
                TeamHundoApiClient client = new TeamHundoApiClient(baseApiUrl);
                teamHundoApiClient = client;
                IList<TeamJson> teams = await Task.Run(() => client.GetTeams());

                connectedTeams = teams;
                teamListBox.ItemsSource = connectedTeams;

                SetTeamStatus("Connected. Select a team, then click Set Team to begin auto-tracking.");
                setTeamButton.IsEnabled = teamListBox.SelectedItem is TeamJson;
            }
            catch (Exception ex)
            {
                SetTeamStatus("Unable to connect: " + ex.Message);
                if (teamHundoApiClient != null)
                {
                    teamHundoApiClient.Dispose();
                    teamHundoApiClient = null;
                }
            }
            finally
            {
                connectButton.IsEnabled = !selectedTeamId.HasValue;
            }
        }

        private async Task SetSelectedTeamAsync(TeamJson selectedTeam)
        {
            if (teamHundoApiClient == null)
            {
                SetTeamStatus("Connect before setting a team.");
                return;
            }

            selectedTeamId = selectedTeam.id;
            selectedTeamName = selectedTeam.name;
            setTeamButton.IsEnabled = false;
            connectButton.IsEnabled = false;
            teamUrlTextBox.IsEnabled = false;
            teamListBox.IsEnabled = false;
            SetTeamStatus("Loading " + selectedTeam.name + " library...");

            if (await LoadSelectedTeamLibrarySnapshotAsync())
            {
                StartTeamFirehose();
                SetTeamStatus("Auto-tracking " + selectedTeam.name + ".");
            }
            else
            {
                selectedTeamId = null;
                selectedTeamName = null;
                connectButton.IsEnabled = true;
                teamUrlTextBox.IsEnabled = true;
                teamListBox.IsEnabled = true;
                setTeamButton.IsEnabled = teamListBox.SelectedItem is TeamJson;
            }
        }

        private void SetTeamStatus(string message)
        {
            teamStatusTextBlock.Text = message;
        }

        private async Task<bool> LoadSelectedTeamLibrarySnapshotAsync()
        {
            if (teamHundoApiClient == null || !selectedTeamId.HasValue)
            {
                return false;
            }

            try
            {
                int teamId = selectedTeamId.Value;
                TeamLibrarySnapshot snapshot = await Task.Run(() =>
                {
                    return new TeamLibrarySnapshot
                    {
                        LibraryContents = teamHundoApiClient.GetLibraryContents(teamId),
                        Library = teamHundoApiClient.GetLibrary(teamId)
                    };
                });

                ownedCardIds.Clear();
                foreach (int cardId in snapshot.LibraryContents)
                {
                    if (cardId > 0)
                    {
                        ownedCardIds.Add(cardId);
                    }
                }

                if (snapshot.Library != null)
                {
                    bewdCount = snapshot.Library.bewdCount;
                }

                RefreshAllCardViews();
                return true;
            }
            catch (Exception ex)
            {
                SetTeamStatus("Unable to load selected team library data: " + ex.Message);
                return false;
            }
        }

        private void ApplyLibraryUpdate(LibraryUpdate update)
        {
            if (update == null || !selectedTeamId.HasValue || update.teamId != selectedTeamId.Value)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                bewdCount = update.bewdCount;
                RefreshCardViews(1);

                if (update.newAcquisitions == null)
                {
                    return;
                }

                foreach (CardAcquisition acquisition in update.newAcquisitions)
                {
                    if (acquisition != null && acquisition.cardId > 0)
                    {
                        ownedCardIds.Add(acquisition.cardId);
                        RefreshCardViews(acquisition.cardId);
                    }
                }
            }));
        }

        private void StartTeamFirehose()
        {
            if (teamHundoApiClient != null && selectedTeamId.HasValue)
            {
                teamHundoApiClient.StartTeamFirehose(ApplyLibraryUpdate, HandleFirehoseStatusChanged);
            }
        }

        private void HandleFirehoseStatusChanged(FirehoseStatusEventArgs statusEventArgs)
        {
            if (statusEventArgs == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                string teamName = string.IsNullOrWhiteSpace(selectedTeamName) ? "selected team" : selectedTeamName;

                if (statusEventArgs.Status == FirehoseStatus.Connected)
                {
                    SetTeamStatus("Auto-tracking " + teamName + ".");
                }
                else if (statusEventArgs.Status == FirehoseStatus.Reconnecting)
                {
                    SetTeamStatus("Auto-tracking " + teamName + " — Reconnecting... (attempt " + statusEventArgs.ReconnectAttempt + " of " + statusEventArgs.MaximumReconnectAttempts + ")");
                }
                else if (statusEventArgs.Status == FirehoseStatus.Failed)
                {
                    SetTeamStatus("Auto-tracking " + teamName + " stopped. " + statusEventArgs.Message);
                    MessageBox.Show(
                        this,
                        statusEventArgs.Message + Environment.NewLine + Environment.NewLine + "Card Overview will now exit.",
                        "Team Hundo Connection Lost",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Application.Current.Shutdown();
                }
            }));
        }

        private class TeamLibrarySnapshot
        {
            public IList<int> LibraryContents { get; set; }
            public LibraryUpdate Library { get; set; }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadCardList();
            teamUrlTextBox.Text = TeamHundoApiClient.DefaultBaseApiUrl;
            LoadSettings();
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is CardWindow)
                    cardWindow = window as CardWindow;
            }

            cardWindow.SetBackgroundColor(backgroundColor);
            cardWindow.Show();

            Resize();

            cards = new List<List<CardView>>();

            for (int i = 0; i < cols; i++)
            {
                List<CardView> colC = new List<CardView>();

                for (int j = 0; j < rows; j++)
                {
                    CardView cv = new CardView(this);
                    cv.SetTbBackgroundColor(tbBackgroundColor);
                    cv.SetTextColor(tbTextColor);
                    colC.Add(cv);

                    Canvas.SetLeft(cv, cardWidth * i);
                    Canvas.SetTop(cv, cardHeight * j);
                    cardWindow.AddCardView(cv);
                }

                cards.Add(colC);
            }

            SetTeamStatus("Connect to Team Hundo, choose a team, then set it to begin auto-tracking.");
        }
    }
}

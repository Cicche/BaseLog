using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using System.Globalization; // in testa al file




namespace BaseLog
{
    public partial class MainWindow : Window
    {
        private string dbPath = @"C:\Temp\BASELogbook.sqlite";
        private DataTable saltiTable;

        public MainWindow()
        {
            InitializeComponent();
            LoadSalti();
        }

        private static bool TryParseAppleSeconds(string raw, out double seconds)
        {
            seconds = 0d;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            // Normalizza: rimuovi spazi, sostituisci eventuale virgola con punto
            string s = raw.Trim().Replace(',', '.');

            // Alcuni dump possono avere più di un punto (es. “589751339.365755 ”): gestiamo solo il primo
            // Parse con InvariantCulture per ignorare le impostazioni locali
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        }

        private static string AppleSecondsToDisplay(double appleSeconds)
        {
            // Scarta la frazione (secondi interi)
            double floored = Math.Floor(appleSeconds);

            // Range-check per sicurezza
            DateTime appleEpoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double max = (DateTime.MaxValue - appleEpoch).TotalSeconds;
            if (floored < 0 || floored > max) return "N/A";

            DateTime dt = appleEpoch.AddSeconds(floored).ToLocalTime();
            return dt.ToString("g");
        }

        private void LoadSalti()
        {
            if (!File.Exists(dbPath))
            {
                MessageBox.Show($"File database non trovato: {dbPath}");
                return;
            }

            using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                conn.Open();
                string query = @"
                    SELECT Z_PK AS id, CAST(ZDATE AS TEXT) AS ZDATE_TEXT, ZJUMPNUMBER, ZOBJECT
                    FROM ZLOGENTRY
                    ORDER BY ZDATE DESC
                ";

                using (var cmd = new SQLiteCommand(query, conn))
                {
                    SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd);
                    saltiTable = new DataTable();
                    adapter.Fill(saltiTable);

                    saltiTable.Columns.Add("DataFormatted", typeof(string));
                    foreach (DataRow row in saltiTable.Rows)
                    {
                        string raw = row["ZDATE_TEXT"]?.ToString();
                        if (TryParseAppleSeconds(raw, out double secs))
                            row["DataFormatted"] = AppleSecondsToDisplay(secs);
                        else
                            row["DataFormatted"] = "N/A";
                    }


                    /*
                    saltiTable.Columns.Add("DataFormatted", typeof(string));
                    foreach (DataRow row in saltiTable.Rows)
                    {
                        if (double.TryParse(row["ZDATE_TEXT"].ToString(), out double appleSecondsDouble))
                        {
                            var convertedDate = ConvertAppleCoreDataTimeToDateTime(appleSecondsDouble);
                            row["DataFormatted"] = convertedDate;
                        }
                        else
                        {
                            row["DataFormatted"] = "N/A";
                        }
                    }*/

                    dataGridSalti.ItemsSource = saltiTable.DefaultView;
                }
            }
        }

        private void DataGridSalti_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dataGridSalti.SelectedItem is DataRowView selectedRow)
            {
                int saltoID = Convert.ToInt32(selectedRow["id"]);
                ShowDettagliSalto(saltoID);
            }
        }

        private void ShowDettagliSalto(int saltoID)
        {
            using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                conn.Open();

                string query = @"
            SELECT 
                CAST(ZDATE AS TEXT) AS ZDATE_TEXT, l.ZJUMPNUMBER, l.ZNOTES,
                o.ZNAME AS OggettoNome, o.ZHEIGHT, o.ZLATITUDE, o.ZLONGITUDE,
                i.ZIMAGE
            FROM ZLOGENTRY l
            LEFT JOIN ZOBJECT o ON l.ZOBJECT = o.Z_PK
            LEFT JOIN ZOBJECTIMAGE i ON o.Z_PK = i.ZOBJECT
            WHERE l.Z_PK = @id
        ";

                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", saltoID);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string raw = reader["ZDATE_TEXT"]?.ToString();
                            string dataDisplay = "N/A";
                            if (TryParseAppleSeconds(raw, out double secs))
                                dataDisplay = AppleSecondsToDisplay(secs);


                            string dettagli = $"Data: {dataDisplay}\n" +
                                               $"Numero salto: {reader["ZJUMPNUMBER"]}\n" +
                                                 $"Note: {reader["ZNOTES"]}\n" +
                                                 $"Oggetto: {reader["OggettoNome"]}\n" +
                                                    $"Altezza: {reader["ZHEIGHT"]}\n" +
                                                 $"Latitudine: {reader["ZLATITUDE"]}\n" +
                                                    $"Longitudine: {reader["ZLONGITUDE"]}";


                            txtDettagliSalto.Text = dettagli;

                            if (reader["ZIMAGE"] != DBNull.Value)
                            {
                                byte[] imageBytes = (byte[])reader["ZIMAGE"];
                                imageOggetto.Source = LoadImage(imageBytes);
                            }
                            else
                            {
                                imageOggetto.Source = null;
                            }
                        }
                        else
                        {
                            txtDettagliSalto.Text = "Salto non trovato.";
                            imageOggetto.Source = null;
                        }
                    }
                }
            }
        }

        public static string ConvertAppleCoreDataTimeToDateTime(double appleSeconds)
        {
            DateTime appleEpoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (appleSeconds < 0 || appleSeconds > (DateTime.MaxValue - appleEpoch).TotalSeconds)
            {
                return ("N/a");
            }
            return appleEpoch.AddSeconds(Math.Floor(appleSeconds)).ToLocalTime().ToString();
        }

        private BitmapImage LoadImage(byte[] imageData)
        {
            using (var ms = new MemoryStream(imageData))
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}

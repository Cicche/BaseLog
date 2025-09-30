using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

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

                    // Colonna di comodo formattata
                    if (!saltiTable.Columns.Contains("DataFormatted"))
                        saltiTable.Columns.Add("DataFormatted", typeof(string));

                    foreach (DataRow row in saltiTable.Rows)
                    {
                        string raw = row["ZDATE_TEXT"]?.ToString();
                        row["DataFormatted"] = AppleSecondsToDisplayFromText(raw);
                    }

                    dataGridSalti.ItemsSource = saltiTable.DefaultView;
                    txtStatus.Text = $"Righe: {saltiTable.Rows.Count}";
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
                        CAST(l.ZDATE AS TEXT) AS ZDATE_TEXT,
                        l.ZJUMPNUMBER, l.ZNOTES,
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
                            string dataDisplay = AppleSecondsToDisplayFromText(reader["ZDATE_TEXT"]?.ToString());

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

        // ===== CSV Export =====
        private void btnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "base_logbook_export.csv" };
            if (dlg.ShowDialog() != true) return;

            var view = (DataView)dataGridSalti.ItemsSource;
            var rows = view?.ToTable().Rows;
            if (rows == null || rows.Count == 0) { MessageBox.Show("Nessun dato da esportare."); return; }

            var sb = new StringBuilder();
            sb.AppendLine("Jump #,Date,Object,Delay (s),Deployment Method,Jump Type,Opening Direction,Rigs,Slider,Pilot Chute,Brake Setting,Notes");

            using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                conn.Open();
                foreach (DataRow r in rows)
                {
                    int id = Convert.ToInt32(r["id"]);
                    string q = @"
                        SELECT CAST(l.ZDATE AS TEXT) AS ZDATE_TEXT, l.ZJUMPNUMBER, l.ZNOTES,
                               o.ZNAME AS OggettoNome
                        FROM ZLOGENTRY l
                        LEFT JOIN ZOBJECT o ON l.ZOBJECT = o.Z_PK
                        WHERE l.Z_PK = @id";
                    using (var cmd = new SQLiteCommand(q, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        using (var rd = cmd.ExecuteReader())
                        {
                            // ...


                            if (rd.Read())
                            {
                                string dateCsv = AppleSecondsToDateIso(rd["ZDATE_TEXT"]?.ToString());
                                string jump = rd["ZJUMPNUMBER"]?.ToString() ?? "";
                                string obj = rd["OggettoNome"]?.ToString() ?? "";
                                string notes = rd["ZNOTES"]?.ToString() ?? "";

                                sb.AppendLine(string.Join(",",
                                    jump,
                                    dateCsv,
                                    CsvEscape(obj),
                                    "", "", "", "", "", "", "", "",
                                    CsvEscape(notes)
                                ));
                            }
                        }
                    }
                }
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            MessageBox.Show("Export completato.");
        }
        private static string AppleSecondsToDateIso(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim().Replace(',', '.');
            double secs;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out secs))
                return "";
            DateTime appleEpoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double floored = Math.Floor(secs);
            if (floored < 0) return "";
            var dt = appleEpoch.AddSeconds(floored).ToLocalTime();
            return dt.ToString("yyyy-MM-dd");
        }


        // ===== CSV Import =====
        private void btnImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv" };
            if (dlg.ShowDialog() != true) return;

            var lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
            if (lines.Length <= 1) { MessageBox.Show("CSV vuoto."); return; }

            var header = ParseCsvLine(lines[0]);
            int idxJump = Array.IndexOf(header, "Jump #");
            int idxDate = Array.IndexOf(header, "Date");
            int idxObject = Array.IndexOf(header, "Object");
            int idxNotes = Array.IndexOf(header, "Notes");
            if (idxJump < 0 || idxDate < 0) { MessageBox.Show("Intestazioni CSV mancanti."); return; }

            using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    // ... codice import che usa conn e tx ...
                    tx.Commit();



                    var findObj = new SQLiteCommand("SELECT Z_PK FROM ZOBJECT WHERE ZNAME = @name LIMIT 1", conn, tx);
                    findObj.Parameters.Add("@name", DbType.String);

                    // Creazione minimale: Z_PK manuale come MAX+1
                    var newObj = new SQLiteCommand("INSERT INTO ZOBJECT (Z_PK, Z_ENT, Z_OPT, ZNAME, ZISACTIVE) VALUES ((SELECT COALESCE(MAX(Z_PK),0)+1 FROM ZOBJECT), 1, 1, @name, 1);", conn, tx);
                    newObj.Parameters.Add("@name", DbType.String);

                    var findLog = new SQLiteCommand("SELECT Z_PK FROM ZLOGENTRY WHERE ZJUMPNUMBER = @jn LIMIT 1", conn, tx);
                    findLog.Parameters.Add("@jn", DbType.Int32);

                    var insLog = new SQLiteCommand("INSERT INTO ZLOGENTRY (Z_PK, Z_ENT, Z_OPT, ZJUMPNUMBER, ZDATE, ZOBJECT, ZNOTES) VALUES ((SELECT COALESCE(MAX(Z_PK),0)+1 FROM ZLOGENTRY), 1, 1, @jn, @date, @obj, @notes)", conn, tx);
                    insLog.Parameters.Add("@jn", DbType.Int32);
                    insLog.Parameters.Add("@date", DbType.Double);
                    insLog.Parameters.Add("@obj", DbType.Int32);
                    insLog.Parameters.Add("@notes", DbType.String);

                    var updLog = new SQLiteCommand("UPDATE ZLOGENTRY SET ZDATE=@date, ZOBJECT=@obj, ZNOTES=@notes WHERE Z_PK=@pk", conn, tx);
                    updLog.Parameters.Add("@date", DbType.Double);
                    updLog.Parameters.Add("@obj", DbType.Int32);
                    updLog.Parameters.Add("@notes", DbType.String);
                    updLog.Parameters.Add("@pk", DbType.Int32);

                    int imported = 0, updated = 0, skipped = 0;

                    for (int i = 1; i < lines.Length; i++)
                    {
                        var fields = ParseCsvLine(lines[i]);
                        if (fields == null || fields.Length < header.Length) { skipped++; continue; }

                        if (!int.TryParse(fields[idxJump]?.Trim(), out int jn)) { skipped++; continue; }

                        // Date CSV yyyy-MM-dd -> Apple seconds (12:00 locali per stabilità)
                        double dateApple;
                        try
                        {
                            var d = DateFromCsv(fields[idxDate]);
                            dateApple = DateTimeToAppleSeconds(d);
                        }
                        catch { skipped++; continue; }

                        // Oggetto
                        int objPk = 0;
                        if (idxObject >= 0)
                        {
                            string objName = fields[idxObject]?.Trim();
                            if (!string.IsNullOrWhiteSpace(objName))
                            {
                                findObj.Parameters["@name"].Value = objName;
                                var existing = findObj.ExecuteScalar();
                                if (existing == null || existing == DBNull.Value)
                                {
                                    newObj.Parameters["@name"].Value = objName;
                                    newObj.ExecuteNonQuery();
                                    // rileggi PK
                                    findObj.Parameters["@name"].Value = objName;
                                    var created = findObj.ExecuteScalar();
                                    if (created != null && created != DBNull.Value)
                                        objPk = Convert.ToInt32(created);
                                }
                                else objPk = Convert.ToInt32(existing);
                            }
                        }

                        string notes = idxNotes >= 0 ? fields[idxNotes] : null;

                        // Upsert per Jump #
                        findLog.Parameters["@jn"].Value = jn;
                        var logPk = findLog.ExecuteScalar();
                        if (logPk == null || logPk == DBNull.Value)
                        {
                            insLog.Parameters["@jn"].Value = jn;
                            insLog.Parameters["@date"].Value = dateApple;
                            insLog.Parameters["@obj"].Value = objPk > 0 ? objPk : (object)DBNull.Value;
                            insLog.Parameters["@notes"].Value = notes ?? (object)DBNull.Value;
                            insLog.ExecuteNonQuery();
                            imported++;
                        }
                        else
                        {
                            updLog.Parameters["@pk"].Value = Convert.ToInt32(logPk);
                            updLog.Parameters["@date"].Value = dateApple;
                            updLog.Parameters["@obj"].Value = objPk > 0 ? objPk : (object)DBNull.Value;
                            updLog.Parameters["@notes"].Value = notes ?? (object)DBNull.Value;
                            updLog.ExecuteNonQuery();
                            updated++;
                        }
                    }

                    tx.Commit();
                    MessageBox.Show($"Import completato. Aggiunti: {imported}, Aggiornati: {updated}, Saltati: {skipped}.");
                    LoadSalti();
                }
            }
        }

        // ===== Helpers =====

        // Parser CSV semplice con supporto virgolette e doppie virgolette
        private static string[] ParseCsvLine(string line)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '\"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '\"') { sb.Append('\"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '\"') inQuotes = true;
                    else if (c == ',') { list.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            list.Add(sb.ToString());
            return list.ToArray();
        }

        private static string CsvEscape(string s)
        {
            if (s == null) return "";
            bool needQuotes = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
            var t = s.Replace("\"", "\"\"");
            return needQuotes ? $"\"{t}\"" : t;
        }

        private static DateTime DateFromCsv(string s)
        {
            if (DateTime.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
            {
                return new DateTime(d.Year, d.Month, d.Day, 12, 0, 0, DateTimeKind.Local);
            }
            throw new FormatException("Data CSV non valida: " + s);
        }

        private static double DateTimeToAppleSeconds(DateTime localDateTime)
        {
            var utc = localDateTime.Kind == DateTimeKind.Utc ? localDateTime : localDateTime.ToUniversalTime();
            DateTime appleEpoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Math.Floor((utc - appleEpoch).TotalSeconds);
        }

        private static string AppleSecondsToDisplayFromText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "N/A";
            string s = raw.Trim().Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double secs))
                return "N/A";
            DateTime appleEpoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double floored = Math.Floor(secs);
            double max = (DateTime.MaxValue - appleEpoch).TotalSeconds;
            if (floored < 0 || floored > max) return "N/A";
            return appleEpoch.AddSeconds(floored).ToLocalTime().ToString("g");
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

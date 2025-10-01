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
//implementazione Mappa
//
using Microsoft.Web.WebView2.Core;
using System.Net.Http;
using System.Threading.Tasks;
//


namespace BaseLog
{
    public partial class MainWindow : Window
    {
        private string dbPath = @"C:\Temp\BASELogbook.sqlite";
        private DataTable saltiTable;
        private DataView saltiView;

        public MainWindow()
        {
            InitializeComponent();
            LoadSalti();
        }

        private void AutoSizeColumns()
        {
            foreach (var c in dataGridSalti.Columns)
            {
                c.Width = DataGridLength.Auto; // misura header
                c.Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells); // misura celle
            }
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
                                  SELECT 
                                    l.Z_PK               AS id,
                                    l.ZJUMPNUMBER        AS NumeroSalto,
                                    CAST(l.ZDATE AS TEXT) AS ZDATE_TEXT,
                                    o.ZNAME              AS Oggetto,
                                    jt.ZNAME             AS TipoSalto,
                                    l.ZNOTES             AS Note
                                  FROM ZLOGENTRY l
                                  LEFT JOIN ZOBJECT   o  ON l.ZOBJECT   = o.Z_PK
                                  LEFT JOIN ZJUMPTYPE jt ON l.ZJUMPTYPE = jt.Z_PK
                                  ORDER BY l.ZDATE DESC
                                ";

                using (var cmd = new SQLiteCommand(query, conn))
                {
                    var adapter = new SQLiteDataAdapter(cmd);
                    saltiTable = new DataTable();
                    adapter.Fill(saltiTable);

                    // Colonna Data formattata per il binding XAML
                    if (!saltiTable.Columns.Contains("Data"))
                        saltiTable.Columns.Add("Data", typeof(string));

                    foreach (DataRow row in saltiTable.Rows)
                    {
                        string raw = row["ZDATE_TEXT"]?.ToString();
                        row["Data"] = AppleSecondsToDisplayFromText(raw);
                    }

                    // Nascondi colonne tecniche non bindate
                    if (saltiTable.Columns.Contains("id"))
                        saltiTable.Columns["id"].ColumnMapping = MappingType.Hidden;
                    if (saltiTable.Columns.Contains("ZDATE_TEXT"))
                        saltiTable.Columns["ZDATE_TEXT"].ColumnMapping = MappingType.Hidden;

                    saltiView = saltiTable.DefaultView;
                    dataGridSalti.ItemsSource = saltiView;
                    AutoSizeColumns(); // opzionale, se hai aggiunto il metodo

                    UpdateStats();
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

        //private void ShowDettagliSalto(int saltoID)
        private async void ShowDettagliSalto(int saltoID)

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

                        // Naviga mappa se possibili coordinate e connessione
                        // Naviga mappa se possibili coordinate e connessione
                        double lat = 0, lon = 0;
                        bool hasLat = false, hasLon = false;

                        // Tenta lettura nativa come numerico
                        try
                        {
                            if (reader["ZLATITUDE"] != DBNull.Value)
                            {
                                var val = reader["ZLATITUDE"];
                                if (val is double dlat) { lat = dlat; hasLat = true; }
                                else if (val is float flat) { lat = flat; hasLat = true; }
                                else if (val is decimal decLat) { lat = (double)decLat; hasLat = true; }
                                else
                                {
                                    var s = val.ToString()?.Trim();
                                    if (!string.IsNullOrEmpty(s))
                                    {
                                        s = s.Replace(',', '.'); // normalizza separatore decimale
                                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                                        {
                                            lat = d; hasLat = true;
                                        }
                                    }
                                }
                            }
                            if (reader["ZLONGITUDE"] != DBNull.Value)
                            {
                                var val = reader["ZLONGITUDE"];
                                if (val is double dlon) { lon = dlon; hasLon = true; }
                                else if (val is float flon) { lon = flon; hasLon = true; }
                                else if (val is decimal decLon) { lon = (double)decLon; hasLon = true; }
                                else
                                {
                                    var s = val.ToString()?.Trim();
                                    if (!string.IsNullOrEmpty(s))
                                    {
                                        s = s.Replace(',', '.'); // normalizza separatore decimale
                                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                                        {
                                            lon = d; hasLon = true;
                                        }
                                    }
                                }
                            }
                           
                        }
                        catch
                        {
                            hasLat = hasLon = false;
                        }

                        await LoadMapWebView2Async(hasLat && hasLon, lat, lon);

                        //LoadMapAsync(hasLat && hasLon, lat, lon);


                    }
                }
            }
        }

        // ========== FILTRI E STATISTICHE ==========
        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
            AutoSizeColumns();
        }

        private void btnSearchReset_Click(object sender, RoutedEventArgs e)
        {
            if (txtSearch != null) txtSearch.Text = "";
            if (saltiView != null) saltiView.RowFilter = "";
            UpdateStats();
            AutoSizeColumns();
        }


        private void ApplyFilters()
        {
            if (saltiView == null) return;

            var filters = new List<string>();

            var dpFromCtrl = FindName("dpFrom") as DatePicker;
            var dpToCtrl = FindName("dpTo") as DatePicker;
            var txtObj = FindName("txtFilterObject") as TextBox;
            var txtNotes = FindName("txtFilterNotes") as TextBox;

            if (dpFromCtrl != null && dpFromCtrl.SelectedDate.HasValue)
            {
                var startLocal = new DateTime(dpFromCtrl.SelectedDate.Value.Year, dpFromCtrl.SelectedDate.Value.Month, dpFromCtrl.SelectedDate.Value.Day, 0, 0, 0, DateTimeKind.Local);
                double startApple = DateTimeToAppleSeconds(startLocal);
                filters.Add($"CONVERT(ZDATE_TEXT, 'System.Double') >= {startApple.ToString(CultureInfo.InvariantCulture)}");
            }
            if (dpToCtrl != null && dpToCtrl.SelectedDate.HasValue)
            {
                var endLocal = new DateTime(dpToCtrl.SelectedDate.Value.Year, dpToCtrl.SelectedDate.Value.Month, dpToCtrl.SelectedDate.Value.Day, 23, 59, 59, DateTimeKind.Local);
                double endApple = DateTimeToAppleSeconds(endLocal);
                filters.Add($"CONVERT(ZDATE_TEXT, 'System.Double') <= {endApple.ToString(CultureInfo.InvariantCulture)}");
            }

            if (txtObj != null && !string.IsNullOrWhiteSpace(txtObj.Text))
            {
                string esc = txtObj.Text.Trim().Replace("'", "''");
                filters.Add($"OggettoNome LIKE '%{esc}%'");
            }

            if (txtNotes != null && !string.IsNullOrWhiteSpace(txtNotes.Text))
            {
                string esc = txtNotes.Text.Trim().Replace("'", "''");
                filters.Add($"ZNOTES LIKE '%{esc}%'");
            }

            saltiView.RowFilter = string.Join(" AND ", filters);
            AutoSizeColumns();
            UpdateStats();
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyUnifiedSearch();
            AutoSizeColumns();
        }
        private void ApplyUnifiedSearch()
        {
            if (saltiView == null) return;

            string q = txtSearch?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(q))
            {
                saltiView.RowFilter = "";
                UpdateStats();
                return;
            }

            // Prova: intervallo numerico "10-20"
            if (TryParseJumpRange(q, out int jFrom, out int jTo))
            {
                saltiView.RowFilter = $"NumeroSalto >= {jFrom} AND NumeroSalto <= {jTo}";
                UpdateStats();
                return;
            }

            // Prova: singolo numero -> NumeroSalto
            if (int.TryParse(q, out int singleJump))
            {
                saltiView.RowFilter = $"NumeroSalto = {singleJump}";
                UpdateStats();
                return;
            }

            // Prova: data singola -> range della giornata
            if (TryParseSingleDate(q, out DateTime dayStart, out DateTime dayEnd))
            {
                double aStart = DateTimeToAppleSeconds(dayStart);
                double aEnd = DateTimeToAppleSeconds(dayEnd);
                saltiView.RowFilter = $"CONVERT(ZDATE_TEXT, 'System.Double') >= {aStart.ToString(CultureInfo.InvariantCulture)} AND CONVERT(ZDATE_TEXT, 'System.Double') <= {aEnd.ToString(CultureInfo.InvariantCulture)}";
                UpdateStats();
                return;
            }

            // Prova: intervallo date "2023-01-01..2023-12-31" o con '-'
            if (TryParseDateRange(q, out DateTime dFrom, out DateTime dTo))
            {
                if (dTo < dFrom) (dFrom, dTo) = (dTo, dFrom);
                // Intero giorno per i capi
                var start = new DateTime(dFrom.Year, dFrom.Month, dFrom.Day, 0, 0, 0, DateTimeKind.Local);
                var end = new DateTime(dTo.Year, dTo.Month, dTo.Day, 23, 59, 59, DateTimeKind.Local);
                double aStart = DateTimeToAppleSeconds(start);
                double aEnd = DateTimeToAppleSeconds(end);
                saltiView.RowFilter = $"CONVERT(ZDATE_TEXT, 'System.Double') >= {aStart.ToString(CultureInfo.InvariantCulture)} AND CONVERT(ZDATE_TEXT, 'System.Double') <= {aEnd.ToString(CultureInfo.InvariantCulture)}";
                UpdateStats();
                return;
            }

            // Ricerca testuale su Oggetto, TipoSalto, Note (tutte le parole devono comparire in almeno uno dei campi)
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var likeParts = new List<string>();
            foreach (var w in words)
            {
                string esc = w.Replace("'", "''");
                likeParts.Add($"(Oggetto LIKE '%{esc}%' OR TipoSalto LIKE '%{esc}%' OR Note LIKE '%{esc}%')");
            }
            saltiView.RowFilter = string.Join(" AND ", likeParts);

            UpdateStats();
        }

        private bool TryParseJumpRange(string input, out int from, out int to)
        {
            from = to = 0;
            // accetta "10-20" o "10..20" con spazi opzionali
            var s = input.Replace(" ", "");
            string[] parts = s.Split(new[] { '-', '–', '—', '.', '…' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int a) && int.TryParse(parts[1], out int b))
            {
                from = Math.Min(a, b);
                to = Math.Max(a, b);
                return true;
            }
            return false;
        }

        private static readonly string[] DateFormats = new[]
{
    "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
    "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy"
};

        private bool TryParseSingleDate(string input, out DateTime start, out DateTime end)
        {
            start = end = default;
            if (DateTime.TryParseExact(input.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                // giornata locale
                start = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Local);
                end = new DateTime(d.Year, d.Month, d.Day, 23, 59, 59, DateTimeKind.Local);
                return true;
            }
            return false;
        }

        private bool TryParseDateRange(string input, out DateTime from, out DateTime to)
        {
            from = to = default;
            var raw = input.Trim();

            // separatori range supportati: ".." o "-"
            string[] sepCandidates = { "..", " - ", "-", " to ", "–", "—" };
            foreach (var sep in sepCandidates)
            {
                var parts = raw.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    if (TryParseAnyDate(parts[0].Trim(), out var d1) && TryParseAnyDate(parts[1].Trim(), out var d2))
                    {
                        from = d1;
                        to = d2;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryParseAnyDate(string s, out DateTime d)
        {
            return DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }


        private void UpdateStats()
        {
            int count = saltiView?.Count ?? 0;

            DateTime? first = null, last = null;
            if (count > 0)
            {
                double minSecs = double.MaxValue;
                double maxSecs = double.MinValue;
                foreach (DataRowView rv in saltiView)
                {
                    string raw = rv["ZDATE_TEXT"]?.ToString();
                    string norm = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().Replace(',', '.');
                    double secs;
                    if (double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out secs))
                    {
                        if (secs < minSecs) minSecs = secs;
                        if (secs > maxSecs) maxSecs = secs;
                    }
                }
                if (minSecs != double.MaxValue) first = AppleSecondsToLocal(minSecs);
                if (maxSecs != double.MinValue) last = AppleSecondsToLocal(maxSecs);
            }

            string range = "";
            if (first.HasValue && last.HasValue)
                range = $" | Dal: {first.Value:yyyy-MM-dd} Al: {last.Value:yyyy-MM-dd}";

            txtStatus.Text = $"Salti: {count}{range}";
        }

        private static DateTime AppleSecondsToLocal(double secs)
        {
            DateTime appleEpoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return appleEpoch.AddSeconds(Math.Floor(secs)).ToLocalTime();
        }

        // ========== EXPORT CSV ==========
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

        // ========== IMPORT CSV ==========
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
                    var findObj = new SQLiteCommand("SELECT Z_PK FROM ZOBJECT WHERE ZNAME = @name LIMIT 1", conn, tx);
                    findObj.Parameters.Add("@name", DbType.String);

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

                        int jn;
                        if (!int.TryParse(fields[idxJump]?.Trim(), out jn)) { skipped++; continue; }

                        double dateApple;
                        try
                        {
                            var d = DateFromCsv(fields[idxDate]);
                            dateApple = DateTimeToAppleSeconds(d);
                        }
                        catch { skipped++; continue; }

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
                                    findObj.Parameters["@name"].Value = objName;
                                    var created = findObj.ExecuteScalar();
                                    if (created != null && created != DBNull.Value)
                                        objPk = Convert.ToInt32(created);
                                }
                                else objPk = Convert.ToInt32(existing);
                            }
                        }

                        string notes = idxNotes >= 0 ? fields[idxNotes] : null;

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
                }
            }

            LoadSalti();
        }

        // ========== SALVA FOTO ==========
        private void btnSavePhoto_Click(object sender, RoutedEventArgs e)
        {
            if (!(dataGridSalti.SelectedItem is DataRowView selectedRow))
            {
                MessageBox.Show("Seleziona un salto dalla lista.");
                return;
            }
            int saltoID = Convert.ToInt32(selectedRow["id"]);

            byte[] imgBytes = null;
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string q = @"
                        SELECT i.ZIMAGE
                        FROM ZLOGENTRY l
                        LEFT JOIN ZOBJECT o ON l.ZOBJECT = o.Z_PK
                        LEFT JOIN ZOBJECTIMAGE i ON o.Z_PK = i.ZOBJECT
                        WHERE l.Z_PK = @id
                        LIMIT 1";
                    using (var cmd = new SQLiteCommand(q, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", saltoID);
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read() && rd["ZIMAGE"] != DBNull.Value)
                            {
                                imgBytes = (byte[])rd["ZIMAGE"];
                            }
                        }
                    }
                }
            }
            catch { /* fallback */ }

            if (imgBytes == null && imageOggetto?.Source != null)
            {
                imgBytes = ExtractBytesFromImageSource(imageOggetto.Source as BitmapSource);
            }

            if (imgBytes == null || imgBytes.Length == 0)
            {
                MessageBox.Show("Nessuna immagine disponibile per questo salto.");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Salva foto",
                Filter = "JPEG Image (*.jpg)|*.jpg|PNG Image (*.png)|*.png",
                FileName = $"salto_{saltoID}"
            };
            if (dlg.ShowDialog() != true) return;

            var ext = System.IO.Path.GetExtension(dlg.FileName)?.ToLowerInvariant();
            if (LooksLikeImageFile(imgBytes))
            {
                File.WriteAllBytes(dlg.FileName, imgBytes);
            }
            else
            {
                BitmapSource bmp = imageOggetto.Source as BitmapSource;
                if (bmp == null)
                {
                    using (var ms = new MemoryStream(imgBytes))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        bmp = bi;
                    }
                }

                if (bmp == null)
                {
                    MessageBox.Show("Impossibile salvare l’immagine.");
                    return;
                }

                using (var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write))
                {
                    if (ext == ".png")
                    {
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        enc.Save(fs);
                    }
                    else
                    {
                        var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                        enc.Frames.Add(BitmapFrame.Create(bmp));
                        enc.Save(fs);
                    }
                }
            }

            MessageBox.Show("Foto salvata correttamente.");
        }

        // ========== HELPERS ==========
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
            DateTime d;
            if (DateTime.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out d))
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
            double secs;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out secs))
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

        private static bool LooksLikeImageFile(byte[] data)
        {
            if (data == null || data.Length < 8) return false;
            if (data[0] == 0xFF && data[1] == 0xD8) return true; // JPEG
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return true; // PNG
            return false;
        }

        private static byte[] ExtractBytesFromImageSource(BitmapSource src)
        {
            if (src == null) return null;
            using (var ms = new MemoryStream())
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                enc.Save(ms);
                return ms.ToArray();
            }
        }

        // ========== implementazione Mappa ==========
        private static async Task<bool> HasInternetAsync(int timeoutMs = 1200)
        {
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource(timeoutMs))
                using (var http = new HttpClient() { Timeout = TimeSpan.FromMilliseconds(timeoutMs) })
                {
                    var resp = await http.GetAsync("http://www.google.com/generate_204", cts.Token);
                    return resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NoContent;
                }
            }
            catch { return false; }
        }

        // Restituisce un URI file:/// al map.html copiato in output (Assets/map.html)
        private static Uri GetMapHtmlUri()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory; // punta a bin\Debug\…
            string path = System.IO.Path.Combine(exeDir, "Assets", "map.html");
            return new Uri(path, UriKind.Absolute); // file:///…/Assets/map.html
        }



        // Costruisce URL OpenStreetMap con marker e zoom 15
        private static string BuildOsmUrl(double lat, double lon, int zoom = 15)
        {
            // https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map={zoom}/{lat}/{lon}
            string latS = lat.ToString("0.000000", CultureInfo.InvariantCulture);
            string lonS = lon.ToString("0.000000", CultureInfo.InvariantCulture);
            return $"https://www.openstreetmap.org/?mlat={latS}&mlon={lonS}#map={zoom}/{latS}/{lonS}";
        }

        private async Task LoadMapWebView2Async(bool hasCoords, double lat, double lon)
        {
            if (txtMapStatus != null) txtMapStatus.Text = "";
            if (wbMap2 == null)
            {
                if (txtMapStatus != null) txtMapStatus.Text = "Controllo mappa non disponibile.";
                return;
            }

            try
            {
                if (wbMap2.CoreWebView2 == null)
                    await wbMap2.EnsureCoreWebView2Async(); // inizializza Edge runtime
                                                            // Se vuoi: wbMap2.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            }
            catch (Exception ex)
            {
                wbMap2.NavigateToString($"<html><body style='font-family:sans-serif;color:#555;padding:10px'>Errore inizializzazione WebView2: {System.Net.WebUtility.HtmlEncode(ex.Message)}</body></html>");
                if (txtMapStatus != null) txtMapStatus.Text = "Errore WebView2";
                return;
            }

            bool online = await HasInternetAsync();
            if (!online)
            {
                wbMap2.NavigateToString("<html><body style='font-family:sans-serif;color:#555;padding:10px'>Nessuna connessione Internet.</body></html>");
                if (txtMapStatus != null) txtMapStatus.Text = "Offline";
                return;
            }

            var baseUri = GetMapHtmlUri();
            if (!File.Exists(baseUri.LocalPath))
            {
                wbMap2.NavigateToString("<html><body style='font-family:sans-serif;color:#555;padding:10px'>map.html non trovato in Assets (output).</body></html>");
                if (txtMapStatus != null) txtMapStatus.Text = "File mancante";
                return;
            }

            if (!hasCoords)
            {
                wbMap2.Source = new Uri(baseUri + "?zoom=2");
                if (txtMapStatus != null) txtMapStatus.Text = "Coordinate non disponibili";
                return;
            }

            string latS = lat.ToString("0.000000", CultureInfo.InvariantCulture);
            string lonS = lon.ToString("0.000000", CultureInfo.InvariantCulture);
            string url = $"{baseUri}?lat={latS}&lon={lonS}&zoom=15";
            wbMap2.Source = new Uri(url);
            if (txtMapStatus != null) txtMapStatus.Text = $"Lat {latS}, Lon {lonS}";
        }

    }
}

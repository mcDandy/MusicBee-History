using System;
using System.Data;
using System.Data.SQLite;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class HistoryControl : UserControl
    {
        private readonly string _dbPath;

        public HistoryControl(string dbPath)
        {
            InitializeComponent();
            _dbPath = dbPath;

            Dock = DockStyle.Fill;
            AutoSize = false;

            Load += HistoryControl_Load;
        }

        private void HistoryControl_Load(object sender, EventArgs e)
        {
            LoadArtistTimeGrid();
            LoadHistoryGrid();
            LoadTopSongsGrid();
        }

        private int GetSettingInt(string id, int defaultValue)
        {
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT VALUE FROM SETTINGS WHERE ID=@id", new SQLiteConnection($"Data Source={_dbPath};Version=3;")))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Connection.Open();
                var result = cmd.ExecuteScalar();
                if (result != null && int.TryParse(result.ToString(), out int value))
                {
                    return value;
                }
            }
            return defaultValue;
        }

        private void ConfigureGrid(DataGridView grid)
        {
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.AlternatingRowsDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(250, 250, 250);
            grid.GridColor = System.Drawing.Color.FromArgb(230, 230, 230);
            grid.CellFormatting += dataGridView_Formatting;
        }

        private void LoadArtistTimeGrid()
        {
            int showSeconds = GetSettingInt("history_time", 30 * 24 * 60 * 60);
            int skipThreshold = GetSettingInt("skip_threshold", 30);
            long minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - showSeconds;

            try
            {
                    var sql = @"WITH BASE_HISTORY AS (
                                    SELECT
                                        h.ID, h.TIME, h.PLAY_HEAD, h.EVENT_TYPE, h.PLAYER_STATE,
                                        ((100.0 + h.SPEED) / 100.0) AS SPEED_MULT,
                                        h.PITCH,
                                        ((100.0 + h.SAMPLE_RATE) / 100.0) AS SAMPLE_RATE_MULT,
                                        tr.TITLE_ID, tr.ARTIST_ID, tr.ALBUM_ID, tr.GENRE_ID, tr.LENGTH,
                                        LAG(tr.TITLE_ID) OVER (ORDER BY h.ID) AS PREV_TITLE_ID,
                                        LAG(tr.ARTIST_ID) OVER (ORDER BY h.ID) AS PREV_ARTIST_ID,
                                        LAG(tr.ALBUM_ID) OVER (ORDER BY h.ID) AS PREV_ALBUM_ID,
                                        LAG(h.EVENT_TYPE) OVER (ORDER BY h.ID) AS PREV_EVENT_TYPE,
                                        LAG(h.PLAY_HEAD) OVER (ORDER BY h.ID) AS PREV_PLAY_HEAD,
                                        LAG(h.TIME) OVER (ORDER BY h.ID) AS PREV_TIME
                                    FROM HISTORY h
                                    LEFT JOIN TRACKS tr ON tr.ID = h.TRACK_ID
                                    WHERE (h.EVENT_TYPE IN (0, 1, 2, 16, 17, 48) OR h.PLAYER_STATE = 3)
                                      AND h.TIME > @MinTime
                                ),
                                SESSIONS AS (
                                    SELECT *,
                                        SUM(CASE
                                            WHEN PREV_EVENT_TYPE IS NULL THEN 1
                                            WHEN EVENT_TYPE = 1 THEN 1
                                            WHEN PREV_TITLE_ID IS NOT NULL AND TITLE_ID IS NOT NULL AND (
                                                 TITLE_ID  IS NOT PREV_TITLE_ID OR
                                                 ARTIST_ID IS NOT PREV_ARTIST_ID OR
                                                 ALBUM_ID  IS NOT PREV_ALBUM_ID) THEN 1
                                            ELSE 0
                                        END) OVER (ORDER BY ID ROWS UNBOUNDED PRECEDING) AS SESSION_ID
                                    FROM BASE_HISTORY
                                ),
                                ORDERED_SESSIONS AS (
                                    SELECT *,
                                        LAG(SESSION_ID) OVER (ORDER BY ID) AS PREV_SESSION_ID
                                    FROM SESSIONS
                                ),
                                CLEANED_DELTAS AS (
                                    SELECT
                                        SESSION_ID, TITLE_ID, ARTIST_ID, ALBUM_ID, TIME, SPEED_MULT, PITCH, SAMPLE_RATE_MULT, LENGTH,
                                        CASE
                                            WHEN PREV_SESSION_ID IS NOT SESSION_ID THEN 0
                                            WHEN TITLE_ID IS NULL THEN 0
                                            WHEN PREV_EVENT_TYPE IN (0, 17) THEN 0
                                            WHEN PREV_PLAY_HEAD IS NULL OR PLAY_HEAD < PREV_PLAY_HEAD THEN 0
                                            WHEN (PLAY_HEAD - PREV_PLAY_HEAD) > ((TIME - PREV_TIME) * 3000.0 * MAX(1.0, SPEED_MULT) * MAX(1.0, SAMPLE_RATE_MULT)) THEN 0
                                            ELSE (PLAY_HEAD - PREV_PLAY_HEAD)
                                        END AS DELTA_POS_MS
                                    FROM ORDERED_SESSIONS
                                ),
                                AGGREGATED_PER_SESSION AS (
                                    SELECT
                                        ARTIST_ID,
                                        SUM(DELTA_POS_MS / (SPEED_MULT * SAMPLE_RATE_MULT)) / 1000.0 AS SessionRealtimeSec,
                                        CASE
                                            WHEN MAX(LENGTH) > 0
                                            THEN (SUM(DELTA_POS_MS) / CAST(MAX(LENGTH) AS REAL)) * 100.0
                                            ELSE 0
                                        END AS SessionPercentage
                                    FROM CLEANED_DELTAS
                                    GROUP BY SESSION_ID, ARTIST_ID
                                    HAVING SUM(DELTA_POS_MS) > 0
                                ),
                                FINAL_SUMS AS (
                                    SELECT
                                        ARTIST_ID,
                                        SUM(SessionRealtimeSec) AS TotalRealtimeSec,
                                        AVG(SessionPercentage) AS AvgPlayPercentage,
                                        COUNT(*) AS TotalSessions,
                                        SUM(CASE WHEN SessionPercentage >= @SkipThreshold THEN 1 ELSE 0 END) AS SongsListened,
                                        SUM(CASE WHEN SessionPercentage < @SkipThreshold THEN 1 ELSE 0 END) AS SongsSkipped
                                    FROM AGGREGATED_PER_SESSION
                                    GROUP BY ARTIST_ID
                                )
                                SELECT
                                    a.VALUE AS ARTIST,
                                    TotalRealtimeSec AS PLAYED_TIME,
                                    AvgPlayPercentage AS PLAY_PERCENTAGE,
                                    TotalSessions AS PLAYED,
                                    SongsListened AS SONGS_LISTENED,
                                    SongsSkipped AS SONGS_SKIPPED,
                                    CAST(SongsSkipped AS REAL) * 100.0 / NULLIF(TotalSessions, 0) AS SKIP_PERCENT
                                FROM FINAL_SUMS f
                                LEFT JOIN ARTISTS a ON a.ID = f.ARTIST_ID
                                ORDER BY TotalRealtimeSec DESC;
                                ";

                    var table = new DataTable();
                    using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@MinTime", minTime);
                        cmd.Parameters.AddWithValue("@SkipThreshold", skipThreshold);
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(table);
                        }
                    }

                    dataGridView1.AutoGenerateColumns = true;
                    dataGridView1.DataSource = table;
                    ConfigureGrid(dataGridView1);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading history (artist): " + ex.ToString());
                }

        }
        private void LoadTopSongsGrid()
        {
            int showSeconds = GetSettingInt("history_time", 30 * 24 * 60 * 60);
            int skipThreshold = GetSettingInt("skip_threshold", 30);
            long minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - showSeconds;

            try
            {
                    var sql = @"WITH BASE_HISTORY AS (
                                    SELECT
                                        h.ID, h.TIME, h.PLAY_HEAD, h.EVENT_TYPE, h.PLAYER_STATE,
                                        ((100.0 + h.SPEED) / 100.0) AS SPEED_MULT,
                                        h.PITCH,
                                        ((100.0 + h.SAMPLE_RATE) / 100.0) AS SAMPLE_RATE_MULT,
                                        tr.TITLE_ID, tr.ARTIST_ID, tr.ALBUM_ID, tr.GENRE_ID, tr.LENGTH,
                                        LAG(tr.TITLE_ID) OVER (ORDER BY h.ID) AS PREV_TITLE_ID,
                                        LAG(tr.ARTIST_ID) OVER (ORDER BY h.ID) AS PREV_ARTIST_ID,
                                        LAG(tr.ALBUM_ID) OVER (ORDER BY h.ID) AS PREV_ALBUM_ID,
                                        LAG(h.EVENT_TYPE) OVER (ORDER BY h.ID) AS PREV_EVENT_TYPE,
                                        LAG(h.PLAY_HEAD) OVER (ORDER BY h.ID) AS PREV_PLAY_HEAD,
                                        LAG(h.TIME) OVER (ORDER BY h.ID) AS PREV_TIME
                                    FROM HISTORY h
                                    LEFT JOIN TRACKS tr ON tr.ID = h.TRACK_ID
                                    WHERE (h.EVENT_TYPE IN (0, 1, 2, 16, 17, 48) OR h.PLAYER_STATE = 3)
                                      AND h.TIME > @MinTime
                                ),
                                SESSIONS AS (
                                    SELECT *,
                                        SUM(CASE
                                            WHEN PREV_EVENT_TYPE IS NULL THEN 1
                                            WHEN EVENT_TYPE = 1 THEN 1
                                            WHEN PREV_TITLE_ID IS NOT NULL AND TITLE_ID IS NOT NULL AND (
                                                 TITLE_ID  IS NOT PREV_TITLE_ID OR
                                                 ARTIST_ID IS NOT PREV_ARTIST_ID OR
                                                 ALBUM_ID  IS NOT PREV_ALBUM_ID) THEN 1
                                            ELSE 0
                                        END) OVER (ORDER BY ID ROWS UNBOUNDED PRECEDING) AS SESSION_ID
                                    FROM BASE_HISTORY
                                ),
                                ORDERED_SESSIONS AS (
                                    SELECT *,
                                        LAG(SESSION_ID) OVER (ORDER BY ID) AS PREV_SESSION_ID
                                    FROM SESSIONS
                                ),
                                CLEANED_DELTAS AS (
                                    SELECT
                                        SESSION_ID, TITLE_ID, ARTIST_ID, ALBUM_ID, TIME, SPEED_MULT, PITCH, SAMPLE_RATE_MULT, LENGTH,
                                        CASE
                                            WHEN PREV_SESSION_ID IS NOT SESSION_ID THEN 0
                                            WHEN TITLE_ID IS NULL THEN 0
                                            WHEN PREV_EVENT_TYPE IN (0, 17) THEN 0
                                            WHEN PREV_PLAY_HEAD IS NULL OR PLAY_HEAD < PREV_PLAY_HEAD THEN 0
                                            WHEN (PLAY_HEAD - PREV_PLAY_HEAD) > ((TIME - PREV_TIME) * 3000.0 * MAX(1.0, SPEED_MULT) * MAX(1.0, SAMPLE_RATE_MULT)) THEN 0
                                            ELSE (PLAY_HEAD - PREV_PLAY_HEAD)
                                        END AS DELTA_POS_MS
                                    FROM ORDERED_SESSIONS
                                ),
                                AGGREGATED_PER_SESSION AS (
                                    SELECT
                                        TITLE_ID, ARTIST_ID, ALBUM_ID,
                                        SUM(DELTA_POS_MS / (SPEED_MULT * SAMPLE_RATE_MULT)) / 1000.0 AS SessionRealtimeSec,
                                        CASE
                                            WHEN MAX(LENGTH) > 0
                                            THEN (SUM(DELTA_POS_MS) / CAST(MAX(LENGTH) AS REAL)) * 100.0
                                            ELSE 0
                                        END AS SessionPercentage
                                    FROM CLEANED_DELTAS
                                    GROUP BY SESSION_ID, TITLE_ID, ARTIST_ID, ALBUM_ID
                                    HAVING SUM(DELTA_POS_MS) > 0
                                ),
                                FINAL_SUMS AS (
                                    SELECT
                                        TITLE_ID, ARTIST_ID, ALBUM_ID,
                                        SUM(SessionRealtimeSec) AS TotalRealtimeSec,
                                        AVG(SessionPercentage) AS AvgPlayPercentage,
                                        COUNT(*) AS TotalSessions,
                                        SUM(CASE WHEN SessionPercentage >= @SkipThreshold THEN 1 ELSE 0 END) AS SongsListened,
                                        SUM(CASE WHEN SessionPercentage < @SkipThreshold THEN 1 ELSE 0 END) AS SongsSkipped
                                    FROM AGGREGATED_PER_SESSION
                                    GROUP BY TITLE_ID, ARTIST_ID, ALBUM_ID
                                )
                                SELECT
                                    a.VALUE AS ARTIST,
                                    al.VALUE AS ALBUM,
                                    ti.VALUE AS TRACK,
                                    TotalRealtimeSec AS PLAYED_TIME,
                                    AvgPlayPercentage AS PLAY_PERCENTAGE,
                                    TotalSessions AS PLAYED,
                                    SongsListened AS SONGS_LISTENED,
                                    SongsSkipped AS SONGS_SKIPPED,
                                    CAST(SongsSkipped AS REAL) * 100.0 / NULLIF(TotalSessions, 0) AS SKIP_PERCENT
                                FROM FINAL_SUMS f
                                    LEFT JOIN ARTISTS a ON a.ID = f.ARTIST_ID
                                    LEFT JOIN ALBUMS al ON al.ID = f.ALBUM_ID
                                    LEFT JOIN TITLES ti ON ti.ID = f.TITLE_ID
                                ORDER BY TotalRealtimeSec DESC;
                                ";

                    var table = new DataTable();
                using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MinTime", minTime);
                    cmd.Parameters.AddWithValue("@SkipThreshold", skipThreshold);
                    using (var adapter = new SQLiteDataAdapter(cmd))
                    {
                        adapter.Fill(table);
                    }
                }

                dataGridView2.AutoGenerateColumns = true;
                dataGridView2.DataSource = table;
                ConfigureGrid(dataGridView2);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading history (songs): " + ex.ToString());
            }
        }
        private void LoadHistoryGrid()
        {
            int showSeconds = GetSettingInt("history_time", 30 * 24 * 60 * 60);
            int skipThreshold = GetSettingInt("skip_threshold", 30);
            long minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - showSeconds;

            try
            {
                string sql = @"WITH BASE_HISTORY AS (
                                   SELECT
                                       h.ID, h.TIME, h.PLAY_HEAD, h.EVENT_TYPE, h.PLAYER_STATE,
                                       ((100.0 + h.SPEED) / 100.0) AS SPEED_MULT,
                                       ((100.0 + h.SAMPLE_RATE) / 100.0) AS SAMPLE_RATE_MULT,
                                       tr.TITLE_ID, tr.ARTIST_ID, tr.ALBUM_ID, tr.LENGTH,
                                       LAG(tr.TITLE_ID) OVER (ORDER BY h.ID) AS PREV_TITLE_ID,
                                       LAG(tr.ARTIST_ID) OVER (ORDER BY h.ID) AS PREV_ARTIST_ID,
                                       LAG(tr.ALBUM_ID) OVER (ORDER BY h.ID) AS PREV_ALBUM_ID,
                                       LAG(h.EVENT_TYPE) OVER (ORDER BY h.ID) AS PREV_EVENT_TYPE,
                                       LAG(h.PLAY_HEAD) OVER (ORDER BY h.ID) AS PREV_PLAY_HEAD,
                                       LAG(h.TIME) OVER (ORDER BY h.ID) AS PREV_TIME
                                   FROM HISTORY h
                                   LEFT JOIN TRACKS tr ON tr.ID = h.TRACK_ID
                                   WHERE (h.EVENT_TYPE IN (0, 1, 2, 16, 17, 48) OR h.PLAYER_STATE = 3)
                                     AND h.TIME > @MinTime
                               ),
                               SESSIONS AS (
                                   SELECT *,
                                       SUM(CASE
                                           WHEN PREV_EVENT_TYPE IS NULL THEN 1
                                           WHEN EVENT_TYPE = 1 THEN 1
                                           WHEN PREV_TITLE_ID IS NOT NULL AND TITLE_ID IS NOT NULL AND (
                                                TITLE_ID  IS NOT PREV_TITLE_ID OR
                                                ARTIST_ID IS NOT PREV_ARTIST_ID OR
                                                ALBUM_ID  IS NOT PREV_ALBUM_ID) THEN 1
                                           ELSE 0
                                       END) OVER (ORDER BY ID ROWS UNBOUNDED PRECEDING) AS SESSION_ID
                                   FROM BASE_HISTORY
                               ),
                               ORDERED_SESSIONS AS (
                                   SELECT *,
                                       LAG(SESSION_ID) OVER (ORDER BY ID) AS PREV_SESSION_ID
                                   FROM SESSIONS
                               ),
                               CLEANED_DELTAS AS (
                                   SELECT
                                       SESSION_ID, TITLE_ID, ARTIST_ID, ALBUM_ID, TIME, SPEED_MULT, SAMPLE_RATE_MULT, LENGTH,
                                       CASE
                                           WHEN PREV_SESSION_ID IS NOT SESSION_ID THEN 0
                                           WHEN TITLE_ID IS NULL THEN 0
                                           WHEN PREV_EVENT_TYPE IN (0, 17) THEN 0
                                           WHEN PREV_PLAY_HEAD IS NULL OR PLAY_HEAD < PREV_PLAY_HEAD THEN 0
                                           WHEN (PLAY_HEAD - PREV_PLAY_HEAD) > ((TIME - PREV_TIME) * 3000.0 * MAX(1.0, SPEED_MULT) * MAX(1.0, SAMPLE_RATE_MULT)) THEN 0
                                           ELSE (PLAY_HEAD - PREV_PLAY_HEAD)
                                       END AS DELTA_POS_MS
                                   FROM ORDERED_SESSIONS
                               ),
                               AGGREGATED AS (
                                   SELECT
                                       TITLE_ID, ARTIST_ID, ALBUM_ID,
                                       MAX(TIME) AS MAX_TIME,
                                       MAX(LENGTH) AS TRACK_LENGTH,
                                       SUM(DELTA_POS_MS) AS SUM_DELTA,
                                       SUM(DELTA_POS_MS / (SPEED_MULT * SAMPLE_RATE_MULT)) AS SUM_REALTIME
                                   FROM CLEANED_DELTAS
                                   GROUP BY SESSION_ID, TITLE_ID, ARTIST_ID, ALBUM_ID
                                   HAVING SUM(DELTA_POS_MS) > 0
                               )
                               SELECT
                                   datetime(agg.MAX_TIME, 'unixepoch', 'localtime') AS TIME,
                                   a.VALUE AS ARTIST,
                                   al.VALUE AS ALBUM,
                                   ti.VALUE AS TRACK,
                                   agg.SUM_REALTIME / 1000.0 AS PLAYED_TIME,
                                   CASE
                                       WHEN agg.TRACK_LENGTH > 0
                                       THEN (agg.SUM_DELTA / CAST(agg.TRACK_LENGTH AS REAL)) * 100.0
                                       ELSE 0.0
                                   END AS PLAY_PERCENTAGE,
                                   CASE
                                       WHEN agg.TRACK_LENGTH > 0
                                            AND (agg.SUM_DELTA / CAST(agg.TRACK_LENGTH AS REAL)) * 100.0 >= @SkipThreshold
                                       THEN 'Listened'
                                       ELSE 'Skipped'
                                   END AS STATUS
                               FROM AGGREGATED agg
                               LEFT JOIN ARTISTS a ON a.ID = agg.ARTIST_ID
                               LEFT JOIN ALBUMS al ON al.ID = agg.ALBUM_ID
                               LEFT JOIN TITLES ti ON ti.ID = agg.TITLE_ID
                               ORDER BY agg.MAX_TIME DESC;";

                var table = new DataTable();
                using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MinTime", minTime);
                    cmd.Parameters.AddWithValue("@SkipThreshold", skipThreshold);
                    using (var adapter = new SQLiteDataAdapter(cmd))
                    {
                        adapter.Fill(table);
                    }
                }
                dataGridView3.AutoGenerateColumns = true;
                dataGridView3.DataSource = table;
                ConfigureGrid(dataGridView3);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading history (full): " + ex.ToString());
            }
        }

        private void HistoryTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (historyTabControl.SelectedTab == tabPage1)
            {
                LoadArtistTimeGrid();
            }
            if (historyTabControl.SelectedTab == tabPage2)
            {
                LoadTopSongsGrid();

            }
            if (historyTabControl.SelectedTab == tabPage3)
            {
                LoadHistoryGrid();
            }
        }

        private void dataGridView2_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void tabPage1_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void tabPage2_Click(object sender, EventArgs e)
        {

        }

        private void tabPage3_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView3_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }
        private void dataGridView_Formatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.Value == null || e.Value == DBNull.Value) return;

            string columnName = ((DataGridView)sender).Columns[e.ColumnIndex].Name;

            if (columnName == "PLAY_PERCENTAGE" || columnName == "SKIP_PERCENT")
            {
                if (double.TryParse(e.Value.ToString(), out double percent))
                {
                    e.Value = $"{percent:F1} %";
                    e.FormattingApplied = true;
                }
            }
            else if (columnName == "PLAYED_TIME")
            {
                if (double.TryParse(e.Value.ToString(), out double totalSeconds))
                {
                    int seconds = (int)totalSeconds;
                    int days = seconds / 86400;
                    int remainder = seconds % 86400;

                    TimeSpan ts = TimeSpan.FromSeconds(remainder);
                    string timePart = ts.ToString(@"hh\:mm\:ss");
                    e.Value = days > 0 ? $"{days} d, {timePart}" : timePart;
                    e.FormattingApplied = true;
                }
            }
        }
    }
}

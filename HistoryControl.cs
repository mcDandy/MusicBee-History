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

        private void LoadArtistTimeGrid()
        {
            long minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30 * 24 * 60 * 60;
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT VALUE FROM SETTINGS WHERE ID='history_time'", new SQLiteConnection($"Data Source={_dbPath};Version=3;")))
            {
                cmd.Connection.Open();
                var result = cmd.ExecuteScalar();
                if (result != null && int.TryParse(result.ToString(), out int seconds))
                {
                    minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds;
                }
                else
                {
                    minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30 * 24 * 60 * 60; // Default to 30 days
                }
            }
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
                                            THEN MIN((SUM(DELTA_POS_MS) / CAST(MAX(LENGTH) AS REAL)) * 100.0, 100.0)
                                            ELSE 0 
                                        END AS SessionPercentage
                                    FROM CLEANED_DELTAS
                                    GROUP BY SESSION_ID, ARTIST_ID
                                    HAVING SUM(DELTA_POS_MS) > 0
                                ),
                                FINAL_SUMS AS (
                                    -- Změna: Seskupujeme pouze podle ARTIST_ID
                                    SELECT
                                        ARTIST_ID,
                                        SUM(SessionRealtimeSec) AS TotalRealtimeSec,
                                        AVG(SessionPercentage) AS AvgPlayPercentage
                                    FROM AGGREGATED_PER_SESSION
                                    GROUP BY ARTIST_ID
                                )
                                SELECT
                                    a.VALUE AS ARTIST,
                                    
                                    -- Dynamické formátování: pokud čas přesáhne 24 hodin (86400 sekund), přidá dny
                                    CASE 
                                        WHEN CAST(TotalRealtimeSec AS INT) / 86400 > 0 
                                        THEN (CAST(TotalRealtimeSec AS INT) / 86400) || ' d, ' || time(CAST(TotalRealtimeSec AS INT) % 86400, 'unixepoch')
                                        ELSE time(CAST(TotalRealtimeSec AS INT), 'unixepoch')
                                    END AS TIME,
                                    
                                    ROUND(AvgPlayPercentage, 1) || ' %' AS PLAY_PERCENTAGE
                                FROM FINAL_SUMS f
                                LEFT JOIN ARTISTS a ON a.ID = f.ARTIST_ID
                                ORDER BY TotalRealtimeSec DESC;
                                ";

                    var table = new DataTable();
                    using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@MinTime", minTime);
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            adapter.Fill(table);
                        }
                    }

                    dataGridView1.AutoGenerateColumns = true;
                    dataGridView1.DataSource = table;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }

        }
        private void LoadTopSongsGrid()
        {
            long minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30 * 24 * 60 * 60;
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT VALUE FROM SETTINGS WHERE ID='history_time'", new SQLiteConnection($"Data Source={_dbPath};Version=3;")))
            {
                cmd.Connection.Open();
                var result = cmd.ExecuteScalar();
                if (result != null && int.TryParse(result.ToString(), out int seconds))
                {
                    minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds;
                }
                else
                {
                    minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30 * 24 * 60 * 60; // Default to 30 days
                }
            }
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
                                            THEN MIN((SUM(DELTA_POS_MS) / CAST(MAX(LENGTH) AS REAL)) * 100.0, 100.0)
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
                                        AVG(SessionPercentage) AS AvgPlayPercentage
                                    FROM AGGREGATED_PER_SESSION
                                    GROUP BY TITLE_ID, ARTIST_ID, ALBUM_ID
                                )
                                -- Finální výstup s překladem ID na texty, formátem dnů a procentem ukončení
                                SELECT
                                    a.VALUE AS ARTIST,
                                    al.VALUE AS ALBUM,
                                    ti.VALUE AS TRACK,
                                    
                                    CASE 
                                        WHEN CAST(TotalRealtimeSec AS INT) / 86400 > 0 
                                        THEN (CAST(TotalRealtimeSec AS INT) / 86400) || ' d, ' || time(CAST(TotalRealtimeSec AS INT) % 86400, 'unixepoch')
                                        ELSE time(CAST(TotalRealtimeSec AS INT), 'unixepoch')
                                    END AS TIME,
                                    
                                    ROUND(AvgPlayPercentage, 1) || ' %' AS PLAY_PERCENTAGE
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
                    using (var adapter = new SQLiteDataAdapter(cmd))
                    {
                        adapter.Fill(table);
                    }
                }

                dataGridView2.AutoGenerateColumns = true;
                dataGridView2.DataSource = table;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }
        private void LoadHistoryGrid()
        {
            long minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30 * 24 * 60 * 60; // Default to 30 days

            using (SQLiteCommand cmd = new SQLiteCommand("SELECT VALUE FROM SETTINGS WHERE ID='history_time'", new SQLiteConnection($"Data Source={_dbPath};Version=3;")))
            {
                cmd.Connection.Open();
                var result = cmd.ExecuteScalar();
                if (result != null && int.TryParse(result.ToString(), out int seconds))
                {
                    minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds;
                }
                else
                {
                    minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30 * 24 * 60 * 60; // Default to 30 days
                }
            }
            try
            {
                string sql = @"WITH BASE_HISTORY AS (
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
                               AGGREGATED AS (
                                   SELECT
                                       TITLE_ID, ARTIST_ID, ALBUM_ID,
                                       MAX(TIME) AS MAX_TIME,
                                       MAX(LENGTH) AS TRACK_LENGTH,
                                       SUM(DELTA_POS_MS) AS SUM_DELTA,
                                       SUM(DELTA_POS_MS / (SPEED_MULT * SAMPLE_RATE_MULT)) AS SUM_REALTIME,
                                       SUM(DELTA_POS_MS * SPEED_MULT) AS SUM_SPEED_MULT,
                                       SUM(DELTA_POS_MS * PITCH) AS SUM_PITCH,
                                       SUM(DELTA_POS_MS * SAMPLE_RATE_MULT) AS SUM_SAMPLE_MULT
                                   FROM CLEANED_DELTAS
                                   GROUP BY SESSION_ID, TITLE_ID, ARTIST_ID, ALBUM_ID
                                   HAVING SUM(DELTA_POS_MS) > 0
                               )
                               SELECT
                                   datetime(agg.MAX_TIME, 'unixepoch', 'localtime') AS TIME,
                                   a.VALUE AS ARTIST,
                                   al.VALUE AS ALBUM,
                                   ti.VALUE AS TRACK,
                                   
                                   CASE 
                                       WHEN CAST(agg.SUM_REALTIME / 1000.0 AS INT) / 86400 > 0 
                                       THEN (CAST(agg.SUM_REALTIME / 1000.0 AS INT) / 86400) || ' d, ' || time(CAST(agg.SUM_REALTIME / 1000.0 AS INT) % 86400, 'unixepoch')
                                       ELSE time(CAST(agg.SUM_REALTIME / 1000.0 AS INT), 'unixepoch')
                                   END AS PLAYED_TIME,
                                   
                                   -- 3. Procento odehrání v této relaci vůči celkové délce skladby (max 100 %)
                                   CASE 
                                       WHEN agg.TRACK_LENGTH > 0 
                                       THEN ROUND(MIN((agg.SUM_DELTA / CAST(agg.TRACK_LENGTH AS REAL)) * 100.0, 100.0), 1) || ' %'
                                       ELSE '0 %'
                                   END AS PLAY_PERCENTAGE,
                                   
                                   CASE 
                                       WHEN ROUND(agg.SUM_SPEED_MULT / agg.SUM_DELTA, 2) != 1.00 
                                            OR ROUND(agg.SUM_PITCH / agg.SUM_DELTA, 2) != 0.00 
                                            OR ROUND(agg.SUM_SAMPLE_MULT / agg.SUM_DELTA, 2) != 1.00
                                       THEN 
                                           SUBSTR(
                                               CASE WHEN ROUND(agg.SUM_SPEED_MULT / agg.SUM_DELTA, 2) != 1.00 THEN ', Speed: ' || ROUND(agg.SUM_SPEED_MULT / agg.SUM_DELTA, 2) || 'x' ELSE '' END ||
                                               CASE WHEN ROUND(agg.SUM_PITCH / agg.SUM_DELTA, 2) != 0.00 THEN ', Pitch: ' || ROUND(agg.SUM_PITCH / agg.SUM_DELTA, 2) ELSE '' END ||
                                               CASE WHEN ROUND(agg.SUM_SAMPLE_MULT / agg.SUM_DELTA, 2) != 1.00 THEN ', Sample: ' || ROUND(agg.SUM_SAMPLE_MULT / agg.SUM_DELTA, 2) || 'x' ELSE '' END,
                                               3
                                           )
                                       ELSE '-'
                                   END AS EFFECTS
                               FROM AGGREGATED agg
                               LEFT JOIN ARTISTS a ON a.ID = agg.ARTIST_ID
                               LEFT JOIN ALBUMS al ON al.ID = agg.ALBUM_ID
                               LEFT JOIN TITLES ti ON ti.ID = agg.TITLE_ID
                               ORDER BY agg.MAX_TIME DESC;
                               ;";

                var table = new DataTable();
                using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MinTime", minTime);
                    using (var adapter = new SQLiteDataAdapter(cmd))
                    {
                        adapter.Fill(table);
                    }
                }
                dataGridView3.AutoGenerateColumns = true;
                dataGridView3.DataSource = table;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
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
    }
}

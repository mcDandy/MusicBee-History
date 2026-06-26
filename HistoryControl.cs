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
            try
            {
                var sql = @"WITH FILTERED_HISTORY AS (
                                SELECT 
                                    h.ID, h.TRACK_ID, h.PLAY_HEAD, h.EVENT_TYPE, h.PLAYER_STATE, h.SPEED, h.SAMPLE_RATE,
                                    tr.ARTIST_ID
                                FROM HISTORY h
                                JOIN TRACKS tr ON tr.ID = h.TRACK_ID
                                WHERE h.TIME > @MinTime
                            ),
                            ORDERED_EVENTS AS (
                                SELECT
                                    fe.*,
                                    LAG(fe.ARTIST_ID) OVER (ORDER BY fe.ID) AS PREV_ARTIST_ID,
                                    LAG(fe.PLAY_HEAD) OVER (ORDER BY fe.ID) AS PREV_PLAY_HEAD,
                                    LAG(fe.EVENT_TYPE) OVER (ORDER BY fe.ID) AS PREV_EVENT_TYPE,
                                    LAG(fe.PLAYER_STATE) OVER (ORDER BY fe.ID) AS PREV_PLAYER_STATE
                                FROM FILTERED_HISTORY fe
                            ),
                            MINUTES_CALCULATION AS (
                                SELECT 
                                    oe.ARTIST_ID,
                                    CASE
                                        WHEN PREV_ARTIST_ID = oe.ARTIST_ID 
                                             AND oe.PLAY_HEAD >= PREV_PLAY_HEAD
                                             AND (
                                                (oe.EVENT_TYPE IN (16, 48) OR (oe.EVENT_TYPE = 2 AND oe.PLAYER_STATE IN (6, 7)))
                                                OR
                                                (PREV_EVENT_TYPE IN (16, 48) OR (PREV_EVENT_TYPE = 2 AND PREV_PLAYER_STATE IN (6, 7)))
                                             )
                                        THEN (oe.PLAY_HEAD - PREV_PLAY_HEAD) 
                                             / ( ((100.0 + oe.SPEED) / 100.0) * ((100.0 + oe.SAMPLE_RATE) / 100.0) )
                                             / 60000.0
                                        ELSE 0
                                    END AS Realtime_Min
                                FROM ORDERED_EVENTS oe
                            ),
                            AGGREGATED AS (
                                SELECT
                                    ARTIST_ID,
                                    SUM(Realtime_Min) AS MinutesPlayed
                                FROM MINUTES_CALCULATION
                                WHERE Realtime_Min > 0
                                GROUP BY ARTIST_ID
                            )
                            SELECT
                                a.VALUE AS Artist,
                                ROUND(agg.MinutesPlayed, 2) AS MinutesPlayed
                            FROM AGGREGATED agg
                            JOIN ARTISTS a ON a.ID = agg.ARTIST_ID
                            ORDER BY MinutesPlayed DESC;";

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
            try
            {
                string sql = @"WITH FILTERED_HISTORY AS (
                                   SELECT
                                       h.ID, h.TRACK_ID, h.PLAY_HEAD, h.EVENT_TYPE, h.PLAYER_STATE, h.SPEED, h.SAMPLE_RATE
                                   FROM HISTORY h
                                   WHERE h.TIME > @MinTime
                               ),
                               ORDERED_EVENTS AS (
                                   SELECT
                                       fe.*,
                                       LAG(fe.TRACK_ID) OVER (ORDER BY fe.ID) AS PREV_TRACK_ID,
                                       LAG(fe.PLAY_HEAD) OVER (ORDER BY fe.ID) AS PREV_PLAY_HEAD,
                                       LAG(fe.EVENT_TYPE) OVER (ORDER BY fe.ID) AS PREV_EVENT_TYPE,
                                       LAG(fe.PLAYER_STATE) OVER (ORDER BY fe.ID) AS PREV_PLAYER_STATE
                                   FROM FILTERED_HISTORY fe
                               ),
                               MINUTES_CALCULATION AS (
                                   SELECT
                                       oe.TRACK_ID,
                                       CASE
                                           WHEN PREV_TRACK_ID = oe.TRACK_ID
                                                AND oe.PLAY_HEAD >= PREV_PLAY_HEAD
                                                AND (
                                                   (oe.EVENT_TYPE IN (16, 48) OR (oe.EVENT_TYPE = 2 AND oe.PLAYER_STATE IN (6, 7)))
                                                   OR
                                                   (PREV_EVENT_TYPE IN (16, 48) OR (PREV_EVENT_TYPE = 2 AND PREV_PLAYER_STATE IN (6, 7)))
                                                )
                                           THEN (oe.PLAY_HEAD - PREV_PLAY_HEAD)
                                                / ( ((100.0 + oe.SPEED) / 100.0) * ((100.0 + oe.SAMPLE_RATE) / 100.0) )
                                                / 60000.0
                                           ELSE 0
                                       END AS Realtime_Min
                                   FROM ORDERED_EVENTS oe
                               ),
                               AGGREGATED AS (
                                   SELECT
                                       TRACK_ID,
                                       SUM(Realtime_Min) AS MinutesPlayed
                                   FROM MINUTES_CALCULATION
                                   WHERE Realtime_Min > 0
                                   GROUP BY TRACK_ID
                               )
                               SELECT
                                   a.VALUE AS Artist,
                                   ti.VALUE AS Title,
                                   ROUND(agg.MinutesPlayed, 2) AS MinutesPlayed
                               FROM AGGREGATED agg
                               JOIN TRACKS tr ON tr.ID = agg.TRACK_ID
                               JOIN ARTISTS a ON a.ID = tr.ARTIST_ID
                               JOIN TITLES ti ON ti.ID = tr.TITLE_ID
                               ORDER BY MinutesPlayed DESC;
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
            long minTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30 * 24 * 60 * 60;
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
                                   WHERE (h.EVENT_TYPE IN (1, 2, 16, 17, 48) OR h.PLAYER_STATE = 3)
                                     AND h.TIME > @MinTime
                               ),
                               SESSIONS AS (
                                   SELECT *,
                                       SUM(CASE
                                           WHEN PREV_EVENT_TYPE IS NULL THEN 1
                                           WHEN PREV_EVENT_TYPE = 17 THEN 1
                                           WHEN PREV_TITLE_ID IS NOT NULL AND TITLE_ID IS NOT NULL AND (
                                                TITLE_ID  IS NOT PREV_TITLE_ID OR
                                                ARTIST_ID IS NOT PREV_ARTIST_ID OR
                                                ALBUM_ID  IS NOT PREV_ALBUM_ID) THEN 1
                                           WHEN PLAY_HEAD < PREV_PLAY_HEAD THEN 1
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
                                       SESSION_ID, TITLE_ID, ARTIST_ID, ALBUM_ID, TIME, SPEED_MULT, PITCH, SAMPLE_RATE_MULT,
                                       CASE
                                           WHEN PREV_SESSION_ID IS NOT SESSION_ID THEN 0
                                           WHEN TITLE_ID IS NULL THEN 0
                                           WHEN PREV_PLAY_HEAD IS NULL OR PLAY_HEAD <= PREV_PLAY_HEAD THEN 0
                                           WHEN (PLAY_HEAD - PREV_PLAY_HEAD) > ((TIME - PREV_TIME) * 3000.0 * MAX(1.0, SPEED_MULT) * MAX(1.0, SAMPLE_RATE_MULT)) THEN 0
                                           ELSE (PLAY_HEAD - PREV_PLAY_HEAD)
                                       END AS DELTA_POS_MS
                                   FROM ORDERED_SESSIONS
                               ),
                               AGGREGATED AS (
                                   SELECT
                                       TITLE_ID, ARTIST_ID, ALBUM_ID,
                                       MAX(TIME) AS MAX_TIME,
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
                                   ROUND(agg.SUM_DELTA / 1000.0, 2) AS PLAYED_LENGTH_S,
                                   ROUND(agg.SUM_REALTIME / 1000.0, 2) AS PLAYED_REALTIME_S,
                                   ROUND(agg.SUM_SPEED_MULT / agg.SUM_DELTA, 4) AS AVG_SPEED_MULT,
                                   ROUND(agg.SUM_PITCH / agg.SUM_DELTA, 3) AS AVG_PITCH,
                                   ROUND(agg.SUM_SAMPLE_MULT / agg.SUM_DELTA, 4) AS AVG_SAMPLE_RATE_MULT
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

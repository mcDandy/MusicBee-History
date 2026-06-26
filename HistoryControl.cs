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
                string sql = @"WITH FILTERED_HISTORY AS (
                                   SELECT
                                       h.ID, h.TIME, h.TRACK_ID, h.PLAY_HEAD AS PLAYED, h.EVENT_TYPE, h.PLAYER_STATE,
                                       ((100.0 + h.SPEED) / 100.0) AS SPEED_MULT, h.PITCH AS PITCH_SEMITONES, ((100.0 + h.SAMPLE_RATE) / 100.0) AS SAMPLE_RATE_MULT,
                                       tr.TITLE_ID, tr.ARTIST_ID, tr.ALBUM_ID, tr.GENRE_ID, tr.LENGTH
                                   FROM HISTORY h
                                   JOIN TRACKS tr ON tr.ID = h.TRACK_ID
                                   WHERE (h.EVENT_TYPE IN (1, 2, 16, 17, 48) OR h.PLAYER_STATE = 3) AND h.TIME > @MinTime
                               ),
                               ORDERED_EVENTS AS (
                                 SELECT fe.*,
                                   LAG(fe.ARTIST_ID) OVER (ORDER BY fe.ID) AS PREV_ARTIST_ID,
                                   LAG(fe.ALBUM_ID) OVER (ORDER BY fe.ID) AS PREV_ALBUM_ID,
                                   LAG(fe.TITLE_ID) OVER (ORDER BY fe.ID) AS PREV_TITLE_ID,
                                   LAG(fe.GENRE_ID) OVER (ORDER BY fe.ID) AS PREV_GENRE_ID,
                                   LAG(fe.LENGTH) OVER (ORDER BY fe.ID) AS PREV_LENGTH,

                                   LAG(fe.PLAYED) OVER (ORDER BY fe.ID) AS PREV_PLAYED,
                                   LAG(fe.TIME) OVER (ORDER BY fe.ID) AS PREV_TIME
                                 FROM FILTERED_HISTORY fe
                               ),
                               EVENT_DELTAS AS (
                                 SELECT oe.*,
                                   SUM(CASE
                                           WHEN PREV_TITLE_ID IS NULL THEN 1
                                           WHEN IFNULL(PREV_ARTIST_ID, -1) != IFNULL(ARTIST_ID, -1) OR
                                                IFNULL(PREV_ALBUM_ID, -1)  != IFNULL(ALBUM_ID, -1)  OR
                                                IFNULL(PREV_TITLE_ID, -1)  != IFNULL(TITLE_ID, -1)  OR
                                                IFNULL(PREV_GENRE_ID, -1)  != IFNULL(GENRE_ID, -1)  OR
                                                IFNULL(PREV_LENGTH, -1)    != IFNULL(LENGTH, -1)    THEN 1
                                           WHEN EVENT_TYPE = 1 AND PLAYED < PREV_PLAYED THEN 1
                                           ELSE 0
                                       END) OVER (ORDER BY ID ROWS UNBOUNDED PRECEDING) AS SESSION_ID
                                 FROM ORDERED_EVENTS oe
                               ),
                               ORDERED_DELTAS AS (
                                 SELECT ed.*,
                                   -- Vytáhneme si SESSION_ID předchozího řádku pro bezpečné určení startu nové skladby
                                   LAG(ed.SESSION_ID) OVER (ORDER BY ed.ID) AS PREV_SESSION_ID
                                 FROM EVENT_DELTAS ed
                               ),
                               CLEAN AS (
                                   SELECT od.SESSION_ID, od.TIME, od.TITLE_ID, od.ARTIST_ID, od.ALBUM_ID,
                                       CASE
                                           -- Pokud se liší SESSION_ID, jsme na prvním řádku nové skladby -> delta je 0
                                           WHEN PREV_SESSION_ID IS NOT NULL AND PREV_SESSION_ID != SESSION_ID THEN 0
                                           WHEN PREV_PLAYED IS NULL THEN 0
                                           WHEN PLAYED <= PREV_PLAYED THEN 0
                                           WHEN PREV_TIME IS NULL THEN 0
                                           WHEN (PLAYED - PREV_PLAYED) > ((TIME - PREV_TIME) * 1000.0 * 3.0 * (CASE WHEN SPEED_MULT < 1.0 THEN 1.0 ELSE SPEED_MULT END) * (CASE WHEN SAMPLE_RATE_MULT < 1.0 THEN 1.0 ELSE SAMPLE_RATE_MULT END)) THEN 0
                                           ELSE (PLAYED - PREV_PLAYED)
                                       END AS DELTA_POS_MS,
                                       SPEED_MULT, PITCH_SEMITONES, SAMPLE_RATE_MULT
                                   FROM ORDERED_DELTAS od
                               ),
                               AGGREGATED AS (
                                   SELECT
                                       SESSION_ID, TITLE_ID, ARTIST_ID, ALBUM_ID,
                                       MAX(CASE WHEN DELTA_POS_MS > 0 THEN TIME END) AS MAX_TIME,
                                       SUM(DELTA_POS_MS) AS SUM_DELTA,
                                       SUM(DELTA_POS_MS / SPEED_MULT / SAMPLE_RATE_MULT) AS SUM_REALTIME,
                                       SUM(DELTA_POS_MS * SPEED_MULT) AS SUM_SPEED_MULT,
                                       SUM(DELTA_POS_MS * PITCH_SEMITONES) AS SUM_PITCH,
                                       SUM(DELTA_POS_MS * SAMPLE_RATE_MULT) AS SUM_SAMPLE_MULT
                                   FROM CLEAN
                                   GROUP BY SESSION_ID, TITLE_ID, ARTIST_ID, ALBUM_ID
                                   HAVING SUM(DELTA_POS_MS) > 0
                               )
                               SELECT
                                   datetime(agg.MAX_TIME, 'unixepoch','localtime') AS TIME,
                                   a.VALUE AS ARTIST, al.VALUE AS ALBUM, ti.VALUE AS TRACK,
                                   ROUND(agg.SUM_DELTA / 1000.0, 2) AS PLAYED_LENGTH_S,
                                   ROUND(agg.SUM_REALTIME / 1000.0, 2) AS PLAYED_REALTIME_S,
                                   ROUND(CASE WHEN agg.SUM_DELTA = 0 THEN 1.0 ELSE agg.SUM_SPEED_MULT / (agg.SUM_DELTA * 1.0) END, 4) AS AVG_SPEED_MULT,
                                   ROUND(CASE WHEN agg.SUM_DELTA = 0 THEN 0.0 ELSE agg.SUM_PITCH / (agg.SUM_DELTA * 1.0) END, 3) AS AVG_PITCH,
                                   ROUND(CASE WHEN agg.SUM_DELTA = 0 THEN 1.0 ELSE agg.SUM_SAMPLE_MULT / (agg.SUM_DELTA * 1.0) END, 4) AS AVG_SAMPLE_RATE_MULT
                               FROM AGGREGATED agg
                               LEFT JOIN ARTISTS a ON a.ID = agg.ARTIST_ID
                               LEFT JOIN ALBUMS al ON al.ID = agg.ALBUM_ID
                               LEFT JOIN TITLES ti ON ti.ID = agg.TITLE_ID
                               ORDER BY agg.MAX_TIME DESC;
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

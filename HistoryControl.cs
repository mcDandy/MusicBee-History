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
            try
            {
                var sql = @"SELECT
                                a.Value AS Artist,
                                ROUND(SUM(Realtime_Min), 2) AS MinutesPlayed
                            FROM (
                                SELECT
                                    tr.Artist_Id,
                                    CASE
                                        WHEN LAG(tr.Artist_Id) OVER (ORDER BY h.Id) = tr.Artist_Id
                                             AND h.PLAY_HEAD >= LAG(h.PLAY_HEAD) OVER (ORDER BY h.Id)
                                             AND (
                                                (h.Event_Type  in (16,48) OR (h.Event_Type = 2 AND h.Player_State IN (6, 7)))
                                                OR
                                                (LAG(h.Event_Type) OVER (ORDER BY h.Id) in (16,48) OR (LAG(h.Event_Type) OVER (ORDER BY h.Id) = 2 AND LAG(h.Player_State) OVER (ORDER BY h.Id) IN (6, 7)))
                                             )
                                        THEN (h.PLAY_HEAD - LAG(h.PLAY_HEAD) OVER (ORDER BY h.Id))
                                             / ( ((100.0 + h.Speed) / 100.0) * ((100.0 + h.Sample_Rate) / 100.0) )
                                             / 60000.0
                                        ELSE 0
                                    END AS Realtime_Min
                                FROM History h
                                JOIN Tracks tr ON h.Track_Id = tr.Id
                            ) h
                            JOIN Artists a ON h.Artist_Id = a.Id
                            WHERE h.Realtime_Min > 0
                            GROUP BY a.Value
                            ORDER BY MinutesPlayed DESC;";

                var table = new DataTable();
                using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
                using (var adapter = new SQLiteDataAdapter(sql, conn))
                {
                    adapter.Fill(table);
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
            try
            {
                string sql = @"SELECT
                                   a.Value AS Artist,
                                   t.Value AS Title,
                                   ROUND(SUM(Realtime_Min), 2) AS MinutesPlayed
                               FROM (
                                   SELECT
                                       tr.Artist_Id,
                                       tr.Title_Id,
                                       CASE
                                           WHEN LAG(tr.Title_Id) OVER (ORDER BY h.Id) = tr.Title_Id 
                                                AND h.PLAY_HEAD >= LAG(h.PLAY_HEAD) OVER (ORDER BY h.Id)
                                                AND (
                                                   (h.Event_Type in (16,48) OR (h.Event_Type = 2 AND h.Player_State IN (6, 7)))
                                                   OR
                                                   (LAG(h.Event_Type) OVER (ORDER BY h.Id) in (16,48) OR (LAG(h.Event_Type) OVER (ORDER BY h.Id) = 2 AND LAG(h.Player_State) OVER (ORDER BY h.Id) IN (6, 7)))
                                                )
                                           THEN (h.PLAY_HEAD - LAG(h.PLAY_HEAD) OVER (ORDER BY h.Id)) 
                                                / ( ((100.0 + h.Speed) / 100.0) * ((100.0 + h.Sample_Rate) / 100.0) )
                                                / 60000.0
                                           ELSE 0
                                       END AS Realtime_Min
                                   FROM History h
                                   JOIN Tracks tr ON h.Track_Id = tr.Id
                               ) h
                               JOIN Artists a ON h.Artist_Id = a.Id
                               JOIN Titles t ON h.Title_Id = t.Id
                               WHERE h.Realtime_Min > 0
                               GROUP BY a.Value, t.Value
                               ORDER BY MinutesPlayed DESC;";

                var table = new DataTable();
                using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
                using (var adapter = new SQLiteDataAdapter(sql, conn))
                {
                    adapter.Fill(table);
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
            try
            {
                string sql = @"WITH FILTERED_HISTORY AS (
                                   SELECT
                                       h.ID, h.TIME, h.TRACK_ID, h.PLAY_HEAD AS PLAYED, h.EVENT_TYPE, h.PLAYER_STATE,
                                       ((100.0 + h.SPEED) / 100.0) AS SPEED_MULT, h.PITCH AS PITCH_SEMITONES, ((100.0 + h.SAMPLE_RATE) / 100.0) AS SAMPLE_RATE_MULT
                                   FROM HISTORY h
                                   WHERE (h.EVENT_TYPE IN (1, 2, 16, 17, 48) OR h.PLAYER_STATE = 3) AND h.TIME > 0
                               ),
                               ORDERED_EVENTS AS (
                                 SELECT fe.*,
                                   tr.TITLE_ID, tr.ARTIST_ID, tr.ALBUM_ID, tr.GENRE_ID, tr.LENGTH,
                                   IFNULL(tr.ARTIST_ID, -1) || ':' || IFNULL(tr.ALBUM_ID, -1) || ':' || IFNULL(tr.TITLE_ID, -1) || ':' || IFNULL(tr.GENRE_ID, -1) || ':' || IFNULL(ROUND(tr.LENGTH / 1000.0), -1) AS LOGICAL_TRACK_KEY,
                                   LAG(IFNULL(tr.ARTIST_ID, -1) || ':' || IFNULL(tr.ALBUM_ID, -1) || ':' || IFNULL(tr.TITLE_ID, -1) || ':' || IFNULL(tr.GENRE_ID, -1) || ':' || IFNULL(ROUND(tr.LENGTH / 1000.0), -1)) OVER (ORDER BY fe.ID) AS PREV_LOGICAL_TRACK_KEY,
                                   LAG(fe.PLAYED) OVER (ORDER BY fe.ID) AS PREV_PLAYED,
                                   LAG(fe.TIME) OVER (ORDER BY fe.ID) AS PREV_TIME
                                 FROM FILTERED_HISTORY fe
                                 JOIN TRACKS tr ON tr.ID = fe.TRACK_ID
                               ),
                               EVENT_DELTAS AS (
                                 SELECT oe.*,
                                   SUM(CASE
                                           WHEN PREV_LOGICAL_TRACK_KEY IS NULL THEN 1
                                           WHEN PREV_LOGICAL_TRACK_KEY <> LOGICAL_TRACK_KEY THEN 1
                                           WHEN EVENT_TYPE = 1 AND PLAYED < PREV_PLAYED THEN 1
                                           ELSE 0
                                       END) OVER (ORDER BY ID ROWS UNBOUNDED PRECEDING) AS SESSION_ID
                                 FROM ORDERED_EVENTS oe
                               ),
                               CLEAN AS (
                                   SELECT ed.SESSION_ID, ed.TIME, ed.LOGICAL_TRACK_KEY, ed.TITLE_ID, ed.ARTIST_ID, ed.ALBUM_ID,
                                       CASE
                                           WHEN PREV_LOGICAL_TRACK_KEY <> LOGICAL_TRACK_KEY THEN 0
                                           WHEN PREV_PLAYED IS NULL THEN 0
                                           WHEN PLAYED <= PREV_PLAYED THEN 0
                                           WHEN PREV_TIME IS NULL THEN 0
                                           WHEN (PLAYED - PREV_PLAYED) > ((TIME - PREV_TIME) * 1000.0 * 3.0 * (CASE WHEN SPEED_MULT < 1.0 THEN 1.0 ELSE SPEED_MULT END) * (CASE WHEN SAMPLE_RATE_MULT < 1.0 THEN 1.0 ELSE SAMPLE_RATE_MULT END)) THEN 0
                                           ELSE (PLAYED - PREV_PLAYED)
                                       END AS DELTA_POS_MS,
                                       SPEED_MULT, PITCH_SEMITONES, SAMPLE_RATE_MULT
                                   FROM EVENT_DELTAS ed
                               ),
                               AGGREGATED AS (
                                   SELECT
                                       SESSION_ID, LOGICAL_TRACK_KEY, TITLE_ID, ARTIST_ID, ALBUM_ID,
                                       MAX(CASE WHEN DELTA_POS_MS > 0 THEN TIME END) AS MAX_TIME,
                                       SUM(DELTA_POS_MS) AS SUM_DELTA,
                                       SUM(DELTA_POS_MS / SPEED_MULT / SAMPLE_RATE_MULT) AS SUM_REALTIME,
                                       SUM(DELTA_POS_MS * SPEED_MULT) AS SUM_SPEED_MULT,
                                       SUM(DELTA_POS_MS * PITCH_SEMITONES) AS SUM_PITCH,
                                       SUM(DELTA_POS_MS * SAMPLE_RATE_MULT) AS SUM_SAMPLE_MULT
                                   FROM CLEAN
                                   GROUP BY SESSION_ID, LOGICAL_TRACK_KEY, TITLE_ID, ARTIST_ID, ALBUM_ID
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
                using (var adapter = new SQLiteDataAdapter(sql, conn))
                {
                    adapter.Fill(table);
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

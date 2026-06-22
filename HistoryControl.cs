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
                string sql = @"
                    WITH RAW_EVENTS AS (
                        SELECT
                            h.ID,
                            h.TIME,
                            h.TRACK_ID,
                            tr.TITLE_ID,
                            tr.ARTIST_ID,
                            tr.ALBUM_ID,
                            a.VALUE  AS ARTIST,
                            al.VALUE AS ALBUM,
                            ti.VALUE AS TRACK,
                            h.PLAY_HEAD AS PLAYED,
                            h.PLAYER_STATE AS PLAYER_STATE,
                            ((100.0 + h.SPEED) / 100.0)       AS SPEED_MULT,
                            h.PITCH                            AS PITCH_SEMITONES,
                            ((100.0 + h.SAMPLE_RATE) / 100.0) AS SAMPLE_RATE_MULT
                        FROM HISTORY h
                        JOIN TRACKS tr ON tr.ID = h.TRACK_ID
                        LEFT JOIN ARTISTS a ON a.ID = tr.ARTIST_ID
                        LEFT JOIN ALBUMS al ON al.ID = tr.ALBUM_ID
                        LEFT JOIN TITLES ti ON ti.ID = tr.TITLE_ID
                        WHERE (h.EVENT_TYPE IN (1, 2, 16, 17, 48) OR h.PLAYER_STATE = 3)
                    ),
                    
                    ORDERED_EVENTS AS (
                      SELECT *,
                        LAG(TRACK_ID) OVER (ORDER BY ID) AS PREV_TRACK_ID,
                        LAG(PLAYED) OVER (ORDER BY ID) AS PREV_PLAYED,
                        LAG(TIME) OVER (ORDER BY ID) AS PREV_TIME
                          FROM RAW_EVENTS
                      ),

                    EVENT_DELTAS AS (
                      SELECT *,
                        SUM(
                            CASE
                                WHEN PREV_TRACK_ID IS NULL THEN 1
                                WHEN PREV_TRACK_ID <> TRACK_ID THEN 1
                                ELSE 0
                            END
                        ) OVER (ORDER BY ID ROWS UNBOUNDED PRECEDING) AS SESSION_ID
                          FROM ORDERED_EVENTS
                      ),
                    
                      CLEAN AS (
                        SELECT
                            SESSION_ID, TIME, ARTIST, ALBUM, TRACK,
                            CASE
                                WHEN PREV_TRACK_ID <> TRACK_ID THEN 0
                                WHEN PREV_PLAYED IS NULL THEN 0
                                WHEN PLAYED <= PREV_PLAYED THEN 0
                                WHEN PREV_TIME IS NULL THEN 0
                                WHEN (PLAYED - PREV_PLAYED) > ((TIME - PREV_TIME) * 1000.0 * 3.0 * (CASE WHEN SPEED_MULT < 1.0 THEN 1.0 ELSE SPEED_MULT END) * (CASE WHEN SAMPLE_RATE_MULT < 1.0 THEN 1.0 ELSE SAMPLE_RATE_MULT END)) THEN 0
                                ELSE (PLAYED - PREV_PLAYED)
                            END AS DELTA_POS_MS,
                            SPEED_MULT, PITCH_SEMITONES, SAMPLE_RATE_MULT
                        FROM EVENT_DELTAS
                    )
                    
                    SELECT
                        datetime(MAX(CASE WHEN DELTA_POS_MS>0 THEN TIME END), 'unixepoch','localtime') AS TIME,
                        ARTIST, ALBUM, TRACK,
                        ROUND(SUM(DELTA_POS_MS) / 1000.0, 2) AS PLAYED_LENGTH_S,
                        ROUND(SUM(DELTA_POS_MS / SPEED_MULT / SAMPLE_RATE_MULT) / 1000.0, 2) AS PLAYED_REALTIME_S,
                        ROUND(CASE WHEN SUM(DELTA_POS_MS) = 0 THEN 1.0 ELSE SUM(DELTA_POS_MS * SPEED_MULT) / (SUM(DELTA_POS_MS) * 1.0) END, 4) AS AVG_SPEED_MULT,
                        ROUND(CASE WHEN SUM(DELTA_POS_MS) = 0 THEN 0.0 ELSE SUM(DELTA_POS_MS * PITCH_SEMITONES) / (SUM(DELTA_POS_MS) * 1.0) END, 3) AS AVG_PITCH,
                        ROUND(CASE WHEN SUM(DELTA_POS_MS) = 0 THEN 1.0 ELSE SUM(DELTA_POS_MS * SAMPLE_RATE_MULT) / (SUM(DELTA_POS_MS) * 1.0) END, 4) AS AVG_SAMPLE_RATE_MULT
                    FROM CLEAN
                    GROUP BY SESSION_ID, ARTIST, ALBUM, TRACK
                    HAVING SUM(DELTA_POS_MS) > 0
                    ORDER BY MAX(CASE WHEN DELTA_POS_MS>0 THEN TIME END) DESC;";

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

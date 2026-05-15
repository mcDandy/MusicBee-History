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
            Load += HistoryControl_Load;
        }

        private void HistoryControl_Load(object sender, EventArgs e)
        {
            LoadArtistTimeGrid();
        }

        private void LoadArtistTimeGrid()
        {
            try
            {
                var sql = @"
                            SELECT 
                                a.Value as Artist,
                                ROUND(SUM(h.min) / 60000.0, 2) as MinutesPlayed
                            FROM (
                                SELECT 
                                    tr.Artist_Id, 
                                    (h.Played - COALESCE(LAG(h.Played) OVER (PARTITION BY h.Track_Id ORDER BY h.Id), 0)) / 
                                    ((100.0 + h.speed) / 100.0) * ((100.0 + h.sample_rate) / 100.0) as min,
                                    h.Time
                                FROM History h
                                JOIN Tracks tr ON h.Track_Id = tr.Id
                                WHERE (h.event_type = 16 OR (h.event_type = 2 AND h.player_state IN (6, 7)))
                            ) h
                            JOIN Artists a ON h.Artist_Id = a.Id
                            WHERE h.min > 0 
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
                string sql = @"
                            SELECT 
                                a.Value as Artist, t.Value as Title,
                                ROUND(SUM(h.min) / 60000.0, 2) as MinutesPlayed
                            FROM (
                                SELECT 
                                    tr.Artist_Id, tr.Title_id, 
                                    (h.Played - COALESCE(LAG(h.Played) OVER (PARTITION BY h.Track_Id ORDER BY h.Id), 0)) / 
                                    ((100.0 + h.speed) / 100.0) * ((100.0 + h.sample_rate) / 100.0) as min,
                                    h.Time
                                FROM History h
                                JOIN Tracks tr ON h.Track_Id = tr.Id
                                WHERE (h.event_type = 16 OR (h.event_type = 2 AND h.player_state IN (6, 7)))
                            ) h
                            JOIN Artists a ON h.Artist_Id = a.Id
                            JOIN Titles t ON h.Title_id = t.Id
                            WHERE h.min > 0 
                            GROUP BY a.Value, t.Value
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
        private void LoadHistoryGrid()
        {
            try
            {
                string sql = @"
                            WITH BASE AS (
                                SELECT
                                    h.ID,
                                    h.TIME,
                                    h.TRACK_ID,
                                    a.VALUE  AS ARTIST,
                                    al.VALUE AS ALBUM,
                                    (h.PLAYED - COALESCE(
                                        (
                                            SELECT h2.PLAYED
                                            FROM HISTORY h2
                                            WHERE h2.TRACK_ID = h.TRACK_ID
                                              AND h2.ID < h.ID
                                            ORDER BY h2.ID DESC
                                            LIMIT 1
                                        ), 0
                                    )) AS DELTA_PLAYED_MS,

                                    ((100.0 + h.SPEED) / 100.0)       AS SPEED_MULT,
                                    h.PITCH                            AS PITCH_SEMITONES,
                                    ((100.0 + h.SAMPLE_RATE) / 100.0) AS SAMPLE_RATE_MULT
                                FROM HISTORY h
                                JOIN TRACKS  tr ON tr.ID = h.TRACK_ID
                                JOIN ARTISTS a  ON a.ID  = tr.ARTIST_ID
                                JOIN ALBUMS  al ON al.ID = tr.ALBUM_ID
                                WHERE (h.EVENT_TYPE = 16 OR (h.EVENT_TYPE = 2 AND h.PLAYER_STATE IN (6, 7))                            )
                            )
                            SELECT
                                datetime(MIN(TIME), 'unixepoch', 'localtime') AS TIME,
                                ARTIST,
                                ALBUM,
                                ROUND(SUM(CASE WHEN DELTA_PLAYED_MS > 0 THEN DELTA_PLAYED_MS ELSE 0 END) / 1000.0, 2) AS PLAYED_LENGTH_S,
                                ROUND(SUM(CASE WHEN DELTA_PLAYED_MS > 0 THEN DELTA_PLAYED_MS ELSE 0 END) / 1000.0 *AVG(SPEED_MULT)*AVG(SAMPLE_RATE_MULT), 2) AS PLAYED_REALTIME_S,
                                ROUND(AVG(SPEED_MULT), 4)       AS AVG_SPEED,
                                ROUND(AVG(PITCH_SEMITONES), 3)  AS AVG_PITCH,
                                ROUND(AVG(SAMPLE_RATE_MULT), 4) AS AVG_SAMPLE_RATE
                            FROM BASE
                            GROUP BY TRACK_ID, ARTIST, ALBUM, date(TIME, 'unixepoch', 'localtime')
                            ORDER BY MIN(TIME) DESC;";

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
}
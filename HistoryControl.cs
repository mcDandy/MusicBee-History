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
                                        THEN (h.PLAY_HEAD - LAG(h.PLAY_HEAD) OVER (ORDER BY h.Id)) 
                                             / ( ((100.0 + h.Speed) / 100.0) * ((100.0 + h.Sample_Rate) / 100.0) ) -- Správné dělení rychlostmi pro reálný čas
                                             / 60000.0 -- Převod z milisekund na minuty
                                        ELSE 0 
                                    END AS Realtime_Min,
                                    h.Event_Type,
                                    h.Player_State
                                FROM History h
                                JOIN Tracks tr ON h.Track_Id = tr.Id
                            ) h
                            JOIN Artists a ON h.Artist_Id = a.Id
                            WHERE h.Realtime_Min > 0 
                              AND (h.Event_Type = 16 OR (h.Event_Type = 2 AND h.Player_State IN (6, 7)))
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
                                           -- Počítáme deltu pouze pokud navazuje stejná skladba (podle stabilního Title_Id)
                                           WHEN LAG(tr.Title_Id) OVER (ORDER BY h.Id) = tr.Title_Id 
                                                AND h.PLAY_HEAD >= LAG(h.PLAY_HEAD) OVER (ORDER BY h.Id) 
                                           THEN (h.PLAY_HEAD - LAG(h.PLAY_HEAD) OVER (ORDER BY h.Id)) 
                                                / ( ((100.0 + h.Speed) / 100.0) * ((100.0 + h.Sample_Rate) / 100.0) ) -- Dělení oběma násobiči pro reálný čas
                                                / 60000.0 -- Převod z milisekund na minuty
                                           ELSE 0 
                                       END AS Realtime_Min,
                                       h.Event_Type,
                                       h.Player_State
                                   FROM History h
                                   JOIN Tracks tr ON h.Track_Id = tr.Id
                               ) h
                               JOIN Artists a ON h.Artist_Id = a.Id
                               JOIN Titles t ON h.Title_Id = t.Id
                               WHERE h.Realtime_Min > 0 
                                 AND (h.Event_Type = 16 OR (h.Event_Type = 2 AND h.Player_State IN (6, 7)))
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
WITH BASE AS (
    SELECT
        h.ID,
        h.TIME,
        h.TRACK_ID,
        a.VALUE  AS ARTIST,
        al.VALUE AS ALBUM,
        ti.VALUE AS TRACK,

        -- předchozí PLAY_HEAD pro stejný TRACK
        COALESCE((
            SELECT h2.PLAY_HEAD
            FROM HISTORY h2
            WHERE h2.TRACK_ID = h.TRACK_ID
              AND h2.ID < h.ID
            ORDER BY h2.ID DESC
            LIMIT 1
        ), 0) AS PREV_PLAY_HEAD,

        (h.PLAY_HEAD) AS PLAY_HEAD,
        ((100.0 + h.SPEED) / 100.0)       AS SPEED_MULT,
        h.PITCH                            AS PITCH_SEMITONES,
        ((100.0 + h.SAMPLE_RATE) / 100.0) AS SAMPLE_RATE_MULT,

        -- DELTA počítáme pouze pokud předchozí záznam pro stejný track existuje a měl stav PLAYING (3)
        CASE
            WHEN EXISTS (
                SELECT 1 FROM HISTORY h3
                WHERE h3.TRACK_ID = h.TRACK_ID AND h3.ID < h.ID
            )
            AND (SELECT h2.PLAYER_STATE
                 FROM HISTORY h2
                 WHERE h2.TRACK_ID = h.TRACK_ID
                   AND h2.ID < h.ID
                 ORDER BY h2.ID DESC
                 LIMIT 1) = 3
            THEN
                CASE
                    WHEN (h.PLAY_HEAD - (SELECT h2.PLAY_HEAD
                                         FROM HISTORY h2
                                         WHERE h2.TRACK_ID = h.TRACK_ID
                                           AND h2.ID < h.ID
                                         ORDER BY h2.ID DESC
                                         LIMIT 1)) > 0
                    THEN (h.PLAY_HEAD - (SELECT h2.PLAY_HEAD
                                         FROM HISTORY h2
                                         WHERE h2.TRACK_ID = h.TRACK_ID
                                           AND h2.ID < h.ID
                                         ORDER BY h2.ID DESC
                                         LIMIT 1))
                    ELSE 0
                END
            ELSE 0
        END AS DELTA_POS_MS
    FROM HISTORY h
    JOIN TRACKS tr ON tr.ID = h.TRACK_ID
    JOIN ARTISTS a ON a.ID = tr.ARTIST_ID
    JOIN ALBUMS al ON al.ID = tr.ALBUM_ID
    JOIN TITLES ti ON ti.ID = tr.TITLE_ID
    WHERE (h.EVENT_TYPE = 16 OR (h.EVENT_TYPE = 2 AND h.PLAYER_STATE IN (6, 7)))
),
CLEAN AS (
    SELECT
        TIME, TRACK_ID, ARTIST, ALBUM, TRACK,
        CASE WHEN DELTA_POS_MS > 0 THEN DELTA_POS_MS ELSE 0 END AS DELTA_POS_MS,
        SPEED_MULT, PITCH_SEMITONES, SAMPLE_RATE_MULT
    FROM BASE
)
SELECT
    datetime(MIN(TIME), 'unixepoch', 'localtime') AS TIME,
    ARTIST, ALBUM, TRACK,
    ROUND(SUM(DELTA_POS_MS) / 1000.0, 2) AS PLAYED_LENGTH_S,
    ROUND(SUM(DELTA_POS_MS * SPEED_MULT * SAMPLE_RATE_MULT) / 1000.0, 2) AS PLAYED_REALTIME_S,
    ROUND(CASE WHEN SUM(DELTA_POS_MS)=0 THEN 1.0 ELSE SUM(DELTA_POS_MS*SPEED_MULT)/SUM(DELTA_POS_MS) END, 4) AS AVG_SPEED_MULT,
    ROUND(CASE WHEN SUM(DELTA_POS_MS)=0 THEN 0.0 ELSE SUM(DELTA_POS_MS*PITCH_SEMITONES)/SUM(DELTA_POS_MS) END, 3) AS AVG_PITCH,
    ROUND(CASE WHEN SUM(DELTA_POS_MS)=0 THEN 1.0 ELSE SUM(DELTA_POS_MS*SAMPLE_RATE_MULT)/SUM(DELTA_POS_MS) END, 4) AS AVG_SAMPLE_RATE_MULT
FROM CLEAN
GROUP BY TRACK_ID, ARTIST, ALBUM, TRACK, date(TIME, 'unixepoch', 'localtime')
ORDER BY MIN(TIME) DESC;";

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
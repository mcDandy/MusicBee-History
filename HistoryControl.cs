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
    h.TRACK_ID AS CUR_TRACK_ID,
    h.PLAY_HEAD,
    h.SPEED,
    h.PITCH,
    h.SAMPLE_RATE,

    -- poslední předchozí playing událost pro stejný TITLE (stabilní identifikátor skladby)
    (SELECT h2.ID
     FROM HISTORY h2
     JOIN TRACKS t2 ON t2.ID = h2.TRACK_ID
     WHERE h2.ID < h.ID
       AND t2.TITLE_ID = (SELECT t3.TITLE_ID FROM TRACKS t3 WHERE t3.ID = h.TRACK_ID)
       AND h2.PLAYER_STATE = 3
     ORDER BY h2.ID DESC
     LIMIT 1) AS LAST_PLAY_ID,

    (SELECT h2.PLAY_HEAD
     FROM HISTORY h2
     WHERE h2.ID = (
       SELECT h3.ID
       FROM HISTORY h3
       JOIN TRACKS tt ON tt.ID = h3.TRACK_ID
       WHERE h3.ID < h.ID
         AND tt.TITLE_ID = (SELECT t4.TITLE_ID FROM TRACKS t4 WHERE t4.ID = h.TRACK_ID)
         AND h3.PLAYER_STATE = 3
       ORDER BY h3.ID DESC
       LIMIT 1
     )
    ) AS PREV_PLAY_HEAD,

    (SELECT h2.TIME
     FROM HISTORY h2
     WHERE h2.ID = (
       SELECT h3.ID
       FROM HISTORY h3
       JOIN TRACKS tt ON tt.ID = h3.TRACK_ID
       WHERE h3.ID < h.ID
         AND tt.TITLE_ID = (SELECT t4.TITLE_ID FROM TRACKS t4 WHERE t4.ID = h.TRACK_ID)
         AND h3.PLAYER_STATE = 3
       ORDER BY h3.ID DESC
       LIMIT 1
     )
    ) AS PREV_PLAY_TIME

  FROM HISTORY h
  WHERE (h.EVENT_TYPE = 16 OR (h.EVENT_TYPE = 2 AND h.PLAYER_STATE IN (6, 7, 3)))
),

ATTR AS (
  -- připojíme metadata podle aktuálního TRACK_ID (pro stabilní TITLE/ARTIST/ALBUM)
  SELECT
    b.TIME,
    b.CUR_TRACK_ID AS OWNER_TRACK_ID,
    t.TITLE_ID, t.ARTIST_ID, t.ALBUM_ID, t.LENGTH,
    b.PLAY_HEAD,
    ((100.0 + b.SPEED) / 100.0) AS SPEED_MULT,
    b.PITCH AS PITCH_SEMITONES,
    ((100.0 + b.SAMPLE_RATE) / 100.0) AS SAMPLE_RATE_MULT,
    b.PREV_PLAY_HEAD,
    b.PREV_PLAY_TIME,
    CASE WHEN b.PREV_PLAY_HEAD IS NULL THEN 0 ELSE (b.PLAY_HEAD - b.PREV_PLAY_HEAD) END AS RAW_DELTA_MS,
    CASE WHEN b.PREV_PLAY_TIME IS NULL THEN NULL ELSE ((b.TIME - b.PREV_PLAY_TIME) * 1000.0) END AS ELAPSED_MS
  FROM BASE b
  LEFT JOIN TRACKS t ON t.ID = b.CUR_TRACK_ID
),

CLEAN AS (
  SELECT
    TIME,
    (SELECT a.VALUE FROM ARTISTS a WHERE a.ID = ATTR.ARTIST_ID) AS ARTIST,
    (SELECT al.VALUE FROM ALBUMS al WHERE al.ID = ATTR.ALBUM_ID) AS ALBUM,
    (SELECT ti.VALUE FROM TITLES ti WHERE ti.ID = ATTR.TITLE_ID) AS TRACK,

    -- ignorujeme delty které jsou:
    --  - neexistující (PREV_PLAY_HEAD NULL),
    --  - negativní nebo 0,
    --  - nebo zjevně jump/seek (RAW_DELTA_MS příliš velké vůči reálnému uplynulému času)
    CASE
      WHEN ATTR.OWNER_TRACK_ID IS NULL THEN 0
      WHEN ATTR.PREV_PLAY_HEAD IS NULL THEN 0
      WHEN ATTR.RAW_DELTA_MS <= 0 THEN 0
      WHEN ATTR.ELAPSED_MS IS NULL THEN 0
      -- pokud je RAW_DELTA_MS mnohem větší než reálný čas * tolerance, jedná se o seek/skip -> ignoruj
      WHEN ATTR.RAW_DELTA_MS > (ATTR.ELAPSED_MS * 3.0 * (CASE WHEN ATTR.SPEED_MULT < 1.0 THEN 1.0 ELSE ATTR.SPEED_MULT END) * (CASE WHEN ATTR.SAMPLE_RATE_MULT < 1.0 THEN 1.0 ELSE ATTR.SAMPLE_RATE_MULT END)) THEN 0
      ELSE
        CASE
          WHEN ATTR.LENGTH IS NOT NULL AND ATTR.LENGTH > 0 AND ATTR.RAW_DELTA_MS > ATTR.LENGTH THEN ATTR.LENGTH
          ELSE ATTR.RAW_DELTA_MS
        END
    END AS DELTA_POS_MS,

    SPEED_MULT,
    PITCH_SEMITONES,
    SAMPLE_RATE_MULT
  FROM ATTR
)

SELECT
  datetime(MIN(CASE WHEN DELTA_POS_MS>0 THEN TIME END), 'unixepoch','localtime') AS TIME,
  ARTIST,
  ALBUM,
  TRACK,
  ROUND(SUM(DELTA_POS_MS) / 1000.0, 2) AS PLAYED_LENGTH_S,
  ROUND(SUM(DELTA_POS_MS * SPEED_MULT * SAMPLE_RATE_MULT) / 1000.0, 2) AS PLAYED_REALTIME_S,
  ROUND(CASE WHEN SUM(DELTA_POS_MS)=0 THEN 1.0 ELSE SUM(DELTA_POS_MS * SPEED_MULT) / SUM(DELTA_POS_MS) END, 4) AS AVG_SPEED_MULT,
  ROUND(CASE WHEN SUM(DELTA_POS_MS)=0 THEN 0.0 ELSE SUM(DELTA_POS_MS * PITCH_SEMITONES) / SUM(DELTA_POS_MS) END, 3) AS AVG_PITCH,
  ROUND(CASE WHEN SUM(DELTA_POS_MS)=0 THEN 1.0 ELSE SUM(DELTA_POS_MS * SAMPLE_RATE_MULT) / SUM(DELTA_POS_MS) END, 4) AS AVG_SAMPLE_RATE_MULT
FROM CLEAN
GROUP BY ARTIST, ALBUM, TRACK
HAVING SUM(DELTA_POS_MS) > 0
ORDER BY MIN(CASE WHEN DELTA_POS_MS>0 THEN TIME END) DESC;";

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
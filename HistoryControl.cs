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
            LoadGrid();
        }

        private void LoadGrid()
        {
            try
            {
                var sql = @"
                            SELECT 
                                tr.Artist_Id, 
                                (h.Played - COALESCE(LAG(h.Played) OVER (PARTITION BY h.Track_Id ORDER BY h.Id), 0)) * 
                                ((100.0 + h.speed) / 100.0) * ((100.0 + h.sample_rate) / 100.0) as Time_played,
                                h.Time
                            FROM History h
                            JOIN Tracks tr ON h.Track_Id = tr.Id
                            WHERE (h.event_type = 16 OR (h.event_type = 2 AND h.player_state IN (6, 7)))
                        ) h
                        JOIN Artists a ON h.Artist_Id = a.Id
                        WHERE h.Time_played > 0 
                        GROUP BY a.Value
                        ORDER BY Listen_time DESC;";

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
    }
}
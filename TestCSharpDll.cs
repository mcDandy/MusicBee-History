using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();
        System.Windows.Forms.ComboBox textBox;
        int? savedSeconds=null;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            try
            {
                mbApiInterface = new MusicBeeApiInterface();
                mbApiInterface.Initialise(apiInterfacePtr);
                about.PluginInfoVersion = PluginInfoVersion;
                about.Name = "Another History Plugin";
                about.Description = "A history plugin for MusicBee. Sees everything that happens in the playhead.";
                about.Author = "mkDaniel";
                about.TargetApplication = "Historyy";   //  the name of a Plugin Storage device or panel header for a dockable panel
                about.Type = PluginType.General;
                about.VersionMajor = 1;  // your plugin version
                about.VersionMinor = 0;
                about.Revision = 1;
                about.MinInterfaceVersion = MinInterfaceVersion;
                about.MinApiRevision = MinApiRevision;
                about.ReceiveNotifications = (ReceiveNotificationFlags)0xff;
                about.ConfigurationPanelHeight = 25;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
                InitDatabase();
                return about;
            }
            catch (Exception ex)
            {
                // Tohle vám řekne přesně, co chybí
                MessageBox.Show(ex.Message + "\n\n" + ex.StackTrace);
                return null;
            }
        }

        public bool Configure(IntPtr panelHandle)
        {
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            if (panelHandle != IntPtr.Zero)
            {
                Panel configPanel = (Panel)Control.FromHandle(panelHandle);
                Label prompt = new Label();
                prompt.AutoSize = true;
                prompt.Location = new Point(0, 0);
                prompt.Text = "time to show:";

                // Přidáme nejprve label, aby WinForms správně propočítal jeho Width
                configPanel.Controls.Add(prompt);

                textBox = new System.Windows.Forms.ComboBox();

                // Použití vestavěného KeyValuePair<string, int>
                var options = new[]
                {
                     new KeyValuePair<string, int>("1 hour", 3600),
                     new KeyValuePair<string, int>("6 hours", 21600),
                     new KeyValuePair<string, int>("1 day", 86400),
                     new KeyValuePair<string, int>("1 week", 604800),
                     new KeyValuePair<string, int>("2 weeks", 1209600),
                     new KeyValuePair<string, int>("1 month", 2592000),
                     new KeyValuePair<string, int>("3 months", 5184000),
                     new KeyValuePair<string, int>("6 months", 15552000),
                     new KeyValuePair<string, int>("1 year", 31536000),
                     new KeyValuePair<string, int>("All time", int.MaxValue)
                };

                textBox.DisplayMember = "Key";
                textBox.ValueMember = "Value";
                textBox.DataSource = options;
                textBox.Bounds = new Rectangle(prompt.Location.X + prompt.Width + 10, 0, 100, textBox.Height);

                string appDataPath = mbApiInterface.Setting_GetPersistentStoragePath();
                string dbFullPath = Path.Combine(appDataPath, DBNAME);

                if (savedSeconds is null)
                {
                    using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbFullPath};Version=3;"))
                    {
                        conn.Open();
                        using (SQLiteCommand c = new SQLiteCommand("SELECT VALUE FROM SETTINGS WHERE ID='history_time'", conn))
                        {
                            object result = c.ExecuteScalar();
                            if (result != null && result != DBNull.Value)
                            {
                                savedSeconds = Convert.ToInt32(result);
                            }
                        }
                    }
                }

                configPanel.Controls.Add(textBox);
                textBox.SelectedValue = savedSeconds ?? options[5].Value;
            }
            return true;
        }

        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            string appDataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            string dbFullPath = Path.Combine(appDataPath, DBNAME);

            if (textBox.SelectedValue != null)
            {
                savedSeconds = (int?)textBox.SelectedValue;

                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbFullPath};Version=3;"))
                {
                    conn.Open();
                    using (SQLiteCommand c = new SQLiteCommand("INSERT OR REPLACE INTO SETTINGS (ID, VALUE) VALUES ('history_time', @value)", conn))
                    {
                        c.Parameters.AddWithValue("@value", savedSeconds);
                        c.ExecuteNonQuery();
                    }
                }
            }
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
            ReceiveNotification("", NotificationType.ShutdownStarted);
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }



        // return an array of lyric or artwork provider names this plugin supports
        // the providers will be iterated through one by one and passed to the RetrieveLyrics/ RetrieveArtwork function in order set by the user in the MusicBee Tags(2) preferences screen until a match is found
        //public string[] GetProviders()
        //{
        //    return null;
        //}

        // return lyrics for the requested artist/title from the requested provider
        // only required if PluginType = LyricsRetrieval
        // return null if no lyrics are found
        //public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album, bool synchronisedPreferred, string provider)
        //{
        //    return null;
        //}

        // return Base64 string representation of the artwork binary data from the requested provider
        // only required if PluginType = ArtworkRetrieval
        // return null if no artwork is found
        //public string RetrieveArtwork(string sourceFileUrl, string albumArtist, string album, string provider)
        //{
        //    //Return Convert.ToBase64String(artworkBinaryData)
        //    return null;
        //}

        //  presence of this function indicates to MusicBee that this plugin has a dockable panel. MusicBee will create the control and pass it as the panel parameter
        //  you can add your own controls to the panel if needed
        //  you can control the scrollable area of the panel using the mbApiInterface.MB_SetPanelScrollableArea function
        //  to set a MusicBee header for the panel, set about.TargetApplication in the Initialise function above to the panel header text
        //public int OnDockablePanelCreated(Control panel)
        //{
        //  //    return the height of the panel and perform any initialisation here
        //  //    MusicBee will call panel.Dispose() when the user removes this panel from the layout configuration
        //  //    < 0 indicates to MusicBee this control is resizable and should be sized to fill the panel it is docked to in MusicBee
        //  //    = 0 indicates to MusicBee this control resizeable
        //  //    > 0 indicates to MusicBee the fixed height for the control.Note it is recommended you scale the height for high DPI screens(create a graphics object and get the DpiY value)
        //    float dpiScaling = 0;
        //    using (Graphics g = panel.CreateGraphics())
        //    {
        //        dpiScaling = g.DpiY / 96f;
        //    }
        //    panel.Paint += panel_Paint;
        //    return Convert.ToInt32(100 * dpiScaling);
        //}

        // presence of this function indicates to MusicBee that the dockable panel created above will show menu items when the panel header is clicked
        // return the list of ToolStripMenuItems that will be displayed
        //public List<ToolStripItem> GetHeaderMenuItems()
        //{
        //    List<ToolStripItem> list = new List<ToolStripItem>();
        //    list.Add(new ToolStripMenuItem("A menu item"));
        //    return list;
        //}

        //private void panel_Paint(object sender, PaintEventArgs e)
        //{
        //    e.Graphics.Clear(Color.Red);
        //    TextRenderer.DrawText(e.Graphics, "hello", SystemFonts.CaptionFont, new Point(10, 10), Color.Blue);
        //}

    }
}

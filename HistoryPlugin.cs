using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MusicBeePlugin;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        static NotificationType lastEvent = NotificationType.PluginStartup;
        static PlayState lastState = PlayState.Stopped;
        static DateTime lastEventTime = DateTime.UtcNow;
        static int lastSpeed = 0;
        static int lastPitch = 0;
        static int lastSampleRate = 0;
        static int lastHeadPos = 0;
        const long unixTicks = 621355968000000000L;
        private const string DBNAME = "MusicBeeHistory.db";

        public void ReceiveNotification(string sourceFileUrl, NotificationType event_type)
        {
            if (new NotificationType[] { NotificationType.PlayStateChanged, NotificationType.TrackChanged, NotificationType.TrackChanging, NotificationType.PluginStartup, NotificationType.ShutdownStarted, NotificationType.TempoSetOrChanged }.Contains(event_type))
            {
                if (event_type == NotificationType.TempoSetOrChanged)
                {
                    string[] urlParts = sourceFileUrl.Split(';');
                    lastSpeed = int.Parse(urlParts[0]);
                    lastPitch = int.Parse(urlParts[1]);
                    lastSampleRate = int.Parse(urlParts[2]);
                }
                PlayState state = mbApiInterface.Player_GetPlayState();
                int played = mbApiInterface.Player_GetPosition();
                int length = mbApiInterface.NowPlaying_GetDuration();
                string artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
                string album = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);
                string title = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
                string genre = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Genre);

                if ((played == 0 && event_type == NotificationType.TrackChanging && lastState == PlayState.Playing) || (played == 0 && state == PlayState.Stopped && lastState == PlayState.Playing))
                {
                    played = lastHeadPos + (int)((DateTime.UtcNow - lastEventTime).TotalMilliseconds*((lastSpeed+100)/100.0 * (lastSampleRate+100)/100.0));
                }
                else if ((played == 0 && event_type == NotificationType.TrackChanging && lastState == PlayState.Paused) || (played == 0 && state == PlayState.Stopped && lastState == PlayState.Paused))
                {
                    played = lastHeadPos;
                }
                else if (played == 0 && event_type == NotificationType.TrackChanging && lastState == PlayState.Stopped)
                {
                    played = lastHeadPos;
                }

                if (new PlayState[] { PlayState.Stopped, PlayState.Paused, PlayState.Playing }.Contains(state))
                {
                    string appDataPath = mbApiInterface.Setting_GetPersistentStoragePath();
                    string dbFullPath = System.IO.Path.Combine(appDataPath, DBNAME);

                    using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbFullPath};Version=3;"))
                    {
                        conn.Open();
                        int? aid = GetOrCreateArtistId(conn, artist);
                        int? alid = GetOrCreateAlbumId(conn, album);
                        int? tid = GetOrCreateTitleId(conn, title);
                        int? gid = GetOrCreateGenreId(conn, genre);
                        int? urli = GetOrCreateUrlId(conn, sourceFileUrl);
                        CreateStateIfNotExists(conn, state);
                        Createevent_typeIfNotExists(conn, event_type);


                        using (SQLiteCommand cmd = conn.CreateCommand())
                        {
                            int? trackId = GetOrCreateTrackId(conn, aid, alid, tid, gid, urli, length);
                            cmd.CommandText =@"INSERT INTO HISTORY 
                                                (TRACK_ID, PLAYER_STATE, EVENT_TYPE, PLAYED, TIME, SPEED, PITCH, SAMPLE_RATE) 
                                                VALUES 
                                                (@track_id, @player_state, @event_type, @played, @Time, @speed, @pitch, @sample_rate);";
                            cmd.Parameters.AddWithValue("@track_id", trackId);
                            cmd.Parameters.AddWithValue("@player_state", (int)state);
                            cmd.Parameters.AddWithValue("@event_type", (int)event_type);
                            cmd.Parameters.AddWithValue("@speed", lastSpeed);
                            cmd.Parameters.AddWithValue("@pitch", lastPitch);
                            cmd.Parameters.AddWithValue("@sample_rate", lastSampleRate);
                            cmd.Parameters.AddWithValue("@played", played);
                            cmd.Parameters.AddWithValue("@Time", (DateTime.UtcNow.Ticks - unixTicks) * 1.0d / TimeSpan.TicksPerSecond);

                            try
                            {
                                cmd.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show(ex.ToString());
                            }
                        }
                    }
                }
                lastEvent = event_type;
                lastState = state;
                lastHeadPos = played;
                lastEventTime = DateTime.UtcNow;
            }



            int? GetOrCreateArtistId(SQLiteConnection conn, string name)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT ID FROM ARTISTS WHERE VALUE = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO ARTISTS (VALUE) VALUES (@name); SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("@name", name);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
                else return null;
            }
            int? GetOrCreateGenreId(SQLiteConnection conn, string name)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT ID FROM GENRES WHERE VALUE = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO GENRES (VALUE) VALUES (@name); SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("@name", name);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
                else return null;
            }
            int? GetOrCreateAlbumId(SQLiteConnection conn, string name)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT ID FROM ALBUMS WHERE VALUE = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO ALBUMS (VALUE) VALUES (@name); SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("@name", name);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
                else return null;
            }
            int? GetOrCreateTitleId(SQLiteConnection conn, string name)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT ID FROM TITLES WHERE VALUE = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO TITLES (VALUE) VALUES (@name); SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("@name", name);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
                else return null;
            }
            int? GetOrCreateTrackId(SQLiteConnection conn, int? aid, int? alid, int? tid, int? gid, int? urli, int? length)
            {
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT ID FROM TRACKS WHERE ARTIST_ID = @aid AND ALBUM_ID = @alid AND TITLE_ID = @tid AND GENRE_ID = @gid AND URL_ID = @urli AND abs(length - @length) < 100";
                    cmd.Parameters.AddWithValue("@aid", aid);
                    cmd.Parameters.AddWithValue("@alid", alid);
                    cmd.Parameters.AddWithValue("@tid", tid);
                    cmd.Parameters.AddWithValue("@gid", gid);
                    cmd.Parameters.AddWithValue("@urli", urli);
                    cmd.Parameters.AddWithValue("@length", length);
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return Convert.ToInt32(result);
                }
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO TRACKS (ARTIST_ID, ALBUM_ID, TITLE_ID, GENRE_ID, URL_ID, LENGTH) VALUES (@aid, @alid, @tid, @gid, @urli, @length); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@aid", aid);
                    cmd.Parameters.AddWithValue("@alid", alid);
                    cmd.Parameters.AddWithValue("@tid", tid);
                    cmd.Parameters.AddWithValue("@gid", gid);
                    cmd.Parameters.AddWithValue("@urli", urli);
                    cmd.Parameters.AddWithValue("@length", length);
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            void CreateStateIfNotExists(SQLiteConnection conn, PlayState state)
            {

                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT ID FROM PLAYER_STATES WHERE VALUE = @name";
                    cmd.Parameters.AddWithValue("@name", state.ToString());
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return;
                }
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO PLAYER_STATES (ID,VALUE) VALUES (@id,@name);";
                    cmd.Parameters.AddWithValue("@id", (int)state);
                    cmd.Parameters.AddWithValue("@name", state.ToString());

                    cmd.ExecuteNonQuery();

                }

            }
            void Createevent_typeIfNotExists(SQLiteConnection conn, NotificationType state)
            {

                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT ID FROM EVENT_TYPES WHERE VALUE = @name";
                    cmd.Parameters.AddWithValue("@name", state.ToString());
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return;
                }
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO EVENT_TYPES (ID,VALUE) VALUES (@id,@name);";
                    cmd.Parameters.AddWithValue("@id", (int)state);
                    cmd.Parameters.AddWithValue("@name", state.ToString());
                    try
                    {
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.ToString());
                    }
                }

            }
            int? GetOrCreateUrlId(SQLiteConnection conn, string url)
            {
                if (!string.IsNullOrEmpty(url))
                {
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT ID FROM URLS WHERE VALUE = @url";
                        cmd.Parameters.AddWithValue("@url", url);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO URLS (VALUE) VALUES (@url); SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("@url", url);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
                else return null;
            }
        }

        private void InitDatabase()
        {
            string appDataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            string dbFullPath = System.IO.Path.Combine(appDataPath, DBNAME);

            using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbFullPath};Version=3;"))
            {
                conn.Open();
                SQLiteCommand command = conn.CreateCommand();
                command.CommandText = @"PRAGMA foreign_keys = ON;";
                command.ExecuteNonQuery();

                command.CommandText = @"CREATE TABLE IF NOT EXISTS ARTISTS (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    VALUE TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS ALBUMS (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    VALUE TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS TITLES (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    VALUE TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS GENRES (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    VALUE TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS PLAYER_STATES (
                    ID INTEGER PRIMARY KEY,
                    VALUE TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS EVENT_TYPES (
                    ID INTEGER PRIMARY KEY,
                    VALUE TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS URLS (
                    ID INTEGER PRIMARY KEY,
                    VALUE TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS TRACKS (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    TITLE_ID INTEGER,
                    ARTIST_ID INTEGER,
                    ALBUM_ID INTEGER,
                    GENRE_ID INTEGER,
                    URL_ID INTEGER,
                    LENGTH INTEGER,
                    FOREIGN KEY(TITLE_ID) REFERENCES TITLES(ID),
                    FOREIGN KEY(ARTIST_ID) REFERENCES ARTISTS(ID),
                    FOREIGN KEY(ALBUM_ID) REFERENCES ALBUMS(ID),
                    FOREIGN KEY(GENRE_ID) REFERENCES GENRES(ID),
                    FOREIGN KEY(URL_ID) REFERENCES URLS(ID)
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS HISTORY (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    TRACK_ID INTEGER,
                    PLAYER_STATE INTEGER,
                    EVENT_TYPE INTEGER,
                    PLAYED INTEGER,
                    TIME REAL,
                    SPEED INTEGER,
                    PITCH INTEGER,
                    SAMPLE_RATE INTEGER,
                    FOREIGN KEY(TRACK_ID) REFERENCES TRACKS(ID),
                    FOREIGN KEY(PLAYER_STATE) REFERENCES PLAYER_STATES(ID),
                    FOREIGN KEY(EVENT_TYPE) REFERENCES EVENT_TYPES(ID)
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE VIEW IF NOT EXISTS HumanReadableHistory AS
                SELECT 
                    h.Id,
                    datetime(h.Time, 'unixepoch', 'localtime') AS Event_time,
                    a.Value AS Artist,
                    ti.Value AS Title,
                    al.Value AS Album,
                    g.Value AS Genre,
                    ps.Value AS Player_state,
                    et.Value AS Event_type,
                    ROUND(h.Played / 1000.0, 2) AS Seconds_played,
                    ROUND(tr.Length / 1000.0, 2) AS Lenght_of_media_s,
                    ROUND((h.Played * 100.0) / tr.Length, 1) AS percent_played
                FROM History h
                LEFT JOIN TRACKS tr ON h.TRACK_ID = tr.ID
                LEFT JOIN ARTISTS a ON tr.ARTIST_ID = a.ID
                LEFT JOIN TITLES ti ON tr.TITLE_ID = ti.ID
                LEFT JOIN ALBUMS al ON tr.ALBUM_ID = al.ID
                LEFT JOIN GENRES g ON tr.GENRE_ID = g.ID
                LEFT JOIN PLAYER_STATES ps ON h.PLAYER_STATE = ps.ID
                LEFT JOIN EVENT_TYPES et ON h.EVENT_TYPE = et.ID
                ORDER BY h.Time DESC;";
                command.ExecuteNonQuery();
            }
        }

        public int OnDockablePanelCreated(Control panel)
        {
            if (panel.InvokeRequired)
            {
                panel.BeginInvoke(new Action(() => OnDockablePanelCreated(panel)));
                return -1;
            }

            panel.Controls.Clear();

            var historyPanel = new HistoryControl(
                Path.Combine(mbApiInterface.Setting_GetPersistentStoragePath(), DBNAME))
            {
                Dock = DockStyle.Fill
            };

            panel.Controls.Add(historyPanel);
            return -1;
        }
    }
}
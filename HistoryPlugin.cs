using System;
using System.Data.SQLite;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Windows.Forms;

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

        static bool mockMode = false;
        static PlayState mockState = PlayState.Stopped;
        static string mockArtist = "Unknown Artist";
        static string mockAlbum = "Unknown Album";
        static string mockTitle = "Unknown Title";
        static int mockDuration = 0;
        static int mockPlayed = 0;
        static string mockGenre = "Unknown Genre";
        static DateTime mockDateTime = DateTime.UtcNow;

        public void RunConversion()
        {
            mockMode = true;
            string dbFullPath = System.IO.Path.Combine(mbApiInterface.Setting_GetPersistentStoragePath(), "MusicBeeHistory.db");
            using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbFullPath};Version=3;"))
            {
                conn.Open();
                SQLiteDataReader reader;
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
    SELECT 
        a.Value AS Artist,
        ti.Value AS Title,
        al.Value AS Album,
        g.Value AS Genre,
        ps.Value AS Player_state,
        et.Value AS Event_type,
        h.Played AS Played,
        CAST(h.Time AS REAL) AS TimeValue,
        h.Length AS Length_of_media,
        u.Value AS Url
    FROM History h
    LEFT JOIN Artists a ON h.Artist = a.Id
    LEFT JOIN Titles ti ON h.Title = ti.Id
    LEFT JOIN Albums al ON h.Album = al.Id
    LEFT JOIN Genres g ON h.Genre = g.Id
    LEFT JOIN player_states ps ON h.player_state = ps.Id
    LEFT JOIN Urls u ON h.Url = u.Id
    LEFT JOIN event_types et ON h.event_type = et.Id;";
                    reader = cmd.ExecuteReader();
                }
                while (reader.Read()) // Prochází řádek po řádku
                {
                    // Teď už můžeš používat indexer přímo na readeru
                    mockArtist = reader["Artist"]?.ToString() ?? "Unknown Artist";
                     mockTitle = reader["Title"]?.ToString() ?? "Unknown Title";
                     mockAlbum = reader["Album"]?.ToString() ?? "Unknown Album";
                     mockGenre = reader["Genre"]?.ToString() ?? "Unknown Genre";

                     mockPlayed = reader["Played"] != DBNull.Value ? Convert.ToInt32(reader["Played"]) : 0;
                     mockDuration = reader["Length_of_media"] != DBNull.Value ? Convert.ToInt32(reader["Length_of_media"]) : 0;

                    // U Enumů pozor - pokud máš v DB text, použij Enum.Parse. Pokud číslo, stačí (PlayState)Convert.ToInt32(...)
                    mockState = reader["Player_state"] != DBNull.Value ?
                        (PlayState)Enum.Parse(typeof(PlayState), reader["Player_state"].ToString()) : PlayState.Stopped;

                    NotificationType mockEvent = reader["Event_type"] != DBNull.Value ?
                        (NotificationType)Enum.Parse(typeof(NotificationType), reader["Event_type"].ToString()) : NotificationType.PluginStartup;

                    // Pokud máš v DB Unix Timestamp jako double (s desetinami), tak raději:
                     double timeValue = Convert.ToDouble(reader["TimeValue"]);
                    long ticks = 621355968000000000L + (long)Math.Round(timeValue * TimeSpan.TicksPerSecond);
                    mockDateTime = new DateTimeOffset(ticks, TimeSpan.Zero).UtcDateTime;

                    string mockUrl = reader["Url"]?.ToString() ?? "Unknown URL";

                    // Tady pak v ReceiveNotification musíš použít tyto "mock" hodnoty místo volání mbApiInterface
                    ReceiveNotification(mockUrl, mockEvent);
                }
            }
            mockMode = false;
        }

        public void ReceiveNotification(string sourceFileUrl, NotificationType event_type)
        {
            if (new NotificationType[] { NotificationType.PlayStateChanged, NotificationType.TrackChanged, NotificationType.TrackChanging, NotificationType.PluginStartup, NotificationType.ShutdownStarted, NotificationType.TempoSetOrChanged }.Contains(event_type))
            {
                if(event_type == NotificationType.TempoSetOrChanged)
                {
                    string[] urlParts = sourceFileUrl.Split(';');
                    lastSpeed = int.Parse(urlParts[0]);
                    lastPitch = int.Parse(urlParts[1]);
                    lastSampleRate = int.Parse(urlParts[2]);
                }
                PlayState state = mbApiInterface.Player_GetPlayState();
                int played = mockMode ? mockPlayed : mbApiInterface.Player_GetPosition();
                int length = mockMode ? mockDuration : mbApiInterface.NowPlaying_GetDuration();
                string artist = mockMode ? mockArtist : mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
                string album = mockMode ? mockAlbum : mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);
                string title = mockMode ? mockTitle : mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
                string genre = mockMode ? mockGenre : mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Genre);

                if ((played == 0 && event_type == NotificationType.TrackChanging && lastState == PlayState.Playing)|| (played == 0 && state == PlayState.Stopped && lastState == PlayState.Playing))
                {
                    played = lastHeadPos + (int)(mockMode ? mockDateTime - lastEventTime : DateTime.UtcNow - lastEventTime).TotalMilliseconds;
                }
                else if ((played == 0 && event_type == NotificationType.TrackChanging && lastState == PlayState.Paused)|| (played == 0 && state == PlayState.Stopped && lastState == PlayState.Paused))
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
                    string dbFullPath = System.IO.Path.Combine(appDataPath, "MusicBeeHistory2.db");

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
                            cmd.CommandText = @"INSERT INTO HISTORY 
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
                            cmd.Parameters.AddWithValue("@Time", (mockMode ? mockDateTime.Ticks : DateTime.UtcNow.Ticks - unixTicks) * 1.0d / TimeSpan.TicksPerSecond);

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
                    // Najdi existujícího interpreta
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT ID FROM ARTISTS WHERE VALUE = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    // Pokud neexistuje, vlož nového
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
                    // Najdi existující žánr
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT ID FROM GENRES WHERE VALUE = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    // Pokud neexistuje, vlož nový žánr
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
                    // Najdi existující album
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT ID FROM ALBUMS WHERE VALUE = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    // Pokud neexistuje, vlož nové album
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
                    // Najdi existující titul
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT ID FROM TITLES WHERE VALUE = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    // Pokud neexistuje, vlož nový titul
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
                    cmd.CommandText = "SELECT ID FROM TRACKS WHERE artist_id = @aid AND album_id = @alid AND title_id = @tid AND genre_id = @gid AND url_id = @urli AND abs(length - @length) < 100";
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
                // Pokud neexistuje, vlož nový titul
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO TRACKS (artist_id, album_id, title_id, genre_id, url_id, length) VALUES (@aid, @alid, @tid, @gid, @urli, @length); SELECT last_insert_rowid();";
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
                    cmd.CommandText = "SELECT Id FROM player_states WHERE Value = @name";
                    cmd.Parameters.AddWithValue("@name", state.ToString());
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return;
                }
                // Pokud neexistuje, vlož novou akci
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO player_states (Id,Value) VALUES (@id,@name);";
                    cmd.Parameters.AddWithValue("@id", (int)state);
                    cmd.Parameters.AddWithValue("@name", state.ToString());

                    cmd.ExecuteNonQuery();

                }

            }
            void Createevent_typeIfNotExists(SQLiteConnection conn, NotificationType state)
            {

                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id FROM event_types WHERE Value = @name";
                    cmd.Parameters.AddWithValue("@name", state.ToString());
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return;
                }
                // Pokud neexistuje, vlož novou akci
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO event_types (Id,Value) VALUES (@id,@name);";
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
                    // Najdi existující URL
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Id FROM Urls WHERE Value = @url";
                        cmd.Parameters.AddWithValue("@url", url);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    // Pokud neexistuje, vlož nové URL
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO Urls (Value) VALUES (@url); SELECT last_insert_rowid();";
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
            string dbFullPath = System.IO.Path.Combine(appDataPath, "MusicBeeHistory2.db");

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
    }
}

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
                    played = lastHeadPos + (int)(DateTime.UtcNow - lastEventTime).TotalMilliseconds;
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
                            cmd.CommandText = @"INSERT INTO History 
                                                (Track, player_state, event_type, Played,Length,Time, Url, Speed, Pitch, SampleRate) 
                                                VALUES 
                                                (@track, @player_state, @event_type, @played, @Length, @Time, @Url, @speed, @pitch, @sample_rate);";
                            cmd.Parameters.AddWithValue("@track", trackId);
                            cmd.Parameters.AddWithValue("@player_state", (int)state);
                            cmd.Parameters.AddWithValue("@event_type", (int)event_type);
                            cmd.Parameters.AddWithValue("@speed", lastSpeed);
                            cmd.Parameters.AddWithValue("@pitch", lastPitch);
                            cmd.Parameters.AddWithValue("@sample_rate", lastSampleRate);
                            cmd.Parameters.AddWithValue("@played", played);
                            cmd.Parameters.AddWithValue("@Time", (mockMode ? mockDateTime.Ticks : DateTime.UtcNow.Ticks - unixTicks) / 10_000_000d);

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
                        cmd.CommandText = "SELECT Id FROM Artists WHERE Value = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    // Pokud neexistuje, vlož nového
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO Artists (Value) VALUES (@name); SELECT last_insert_rowid();";
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
                        cmd.CommandText = "SELECT Id FROM Genres WHERE Value = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    // Pokud neexistuje, vlož nový žánr
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO Genres (Value) VALUES (@name); SELECT last_insert_rowid();";
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
                        cmd.CommandText = "SELECT Id FROM Albums WHERE Value = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    // Pokud neexistuje, vlož nové album
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO Albums (Value) VALUES (@name); SELECT last_insert_rowid();";
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
                        cmd.CommandText = "SELECT Id FROM Titles WHERE Value = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        object result = cmd.ExecuteScalar();
                        if (result != null)
                            return Convert.ToInt32(result);
                    }
                    // Pokud neexistuje, vlož nové album
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO Titles (Value) VALUES (@name); SELECT last_insert_rowid();";
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
                    cmd.CommandText = "SELECT Id FROM Tracks WHERE artist_id = @aid AND album_id = @alid AND title_id = @tid AND genre_id = @gid AND url_id = @urli AND abs(length - @length) < 100";
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
                // Pokud neexistuje, vlož nové album
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO Tracks (artist_id, album_id, title_id, genre_id, url_id, length) VALUES (@aid, @alid, @tid, @gid, @urli, @length); SELECT last_insert_rowid();";
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
            string dbFullPath = System.IO.Path.Combine(appDataPath, "MusicBeeHistory.db");

            using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbFullPath};Version=3;"))
            {
                conn.Open();
                SQLiteCommand command = conn.CreateCommand();
                command.CommandText = @"PRAGMA foreign_keys = ON;";
                command.ExecuteNonQuery();

                command.CommandText = @"CREATE TABLE IF NOT EXISTS Artists (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Value TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS Albums (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Value TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS Titles (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Value TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS Genres (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Value TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS player_states (
                    Id INTEGER PRIMARY KEY,
                    Value TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS event_types (
                    Id INTEGER PRIMARY KEY,
                    Value TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS Urls (
                    Id INTEGER PRIMARY KEY,
                    Value TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS Tracks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title_Id INTEGER,
                    Artist_Id INTEGER,
                    Album_Id INTEGER,
                    Genre_Id INTEGER,
                    Url_Id INTEGER,
                    Length INTEGER,
                    FOREIGN KEY(Title_Id) REFERENCES Titles(Id),
                    FOREIGN KEY(Artist_Id) REFERENCES Artists(Id),
                    FOREIGN KEY(Album_Id) REFERENCES Albums(Id),
                    FOREIGN KEY(Genre_Id) REFERENCES Genres(Id),
                    FOREIGN KEY(Url_Id) REFERENCES Urls(Id)
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS History (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Track INTEGER,
                    player_state integer,
                    event_type integer,
                    Played integer,
                    Time TIMESTAMP DEFAULT(unixepoch('subsec')),
                    speed integer,
                    pitch integer,
                    sample_rate integer,
                    foreign key(Track) references Tracks(Id),
                    foreign key(player_state) references player_states(Id),
                    foreign key(event_type) references event_types(Id)
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
                LEFT JOIN Tracks tr ON h.Track_Id = tr.Id
                LEFT JOIN Artists a ON tr.Artist_Id = a.Id
                LEFT JOIN Titles ti ON tr.Title_Id = ti.Id
                LEFT JOIN Albums al ON tr.Album_Id = al.Id
                LEFT JOIN Genres g ON tr.Genre_Id = g.Id
                LEFT JOIN player_states ps ON h.player_state = ps.Id
                LEFT JOIN event_types et ON h.event_type = et.Id
                ORDER BY h.Time DESC;";
                command.ExecuteNonQuery();
            }
        }
    }
}

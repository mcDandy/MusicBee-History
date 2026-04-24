using System;
using System.Data.SQLite;
using System.Linq;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        public void ReceiveNotification(string sourceFileUrl, NotificationType event_type)
        {
                if (new NotificationType[] { NotificationType.PlayStateChanged, NotificationType.TrackChanged, NotificationType.TrackChanging }.Contains(event_type))
                {
                    PlayState state = mbApiInterface.Player_GetPlayState();
                    float played = mbApiInterface.Player_GetPosition();
                    float length = mbApiInterface.NowPlaying_GetDuration();
                    string artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
                    string album = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);
                    string title = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
                    string genre = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Genre);

                    if (new PlayState[] { PlayState.Stopped, PlayState.Paused, PlayState.Playing }.Contains(state))
                    {
                        string appDataPath = mbApiInterface.Setting_GetPersistentStoragePath();
                        string dbFullPath = System.IO.Path.Combine(appDataPath, "MusicBeeHistory.db");

                        using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbFullPath};Version=3;"))
                        {
                            conn.Open();
                            int? aid = GetOrCreateArtistId(conn, artist);
                            int? alid = GetOrCreateAlbumId(conn, album);
                            int? tid = GetOrCreateTitleId(conn, title);
                            int? gid = GetOrCreateGenreId(conn, genre);
                            int urli = GetOrCreateUrlId(conn, sourceFileUrl);
                            CreateStateIfNotExists(conn, state);
                            Createevent_typeIfNotExists(conn, event_type);


                        using (SQLiteCommand cmd = conn.CreateCommand())
                            {
                            cmd.CommandText = @"INSERT INTO History 
                                                (Artist, Album, Title, Genre, player_state, event_type, Played,Length, Url) 
                                                VALUES 
                                                (@artist, @album, @title, @genre, @player_state, @event_type, @played, @Length, @Url);";
                            cmd.Parameters.AddWithValue("@artist", (object)aid ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@album", (object)alid ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@title", (object)tid ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@genre", (object)gid ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@player_state", (int)state);
                                cmd.Parameters.AddWithValue("@event_type", (int)event_type);
                                cmd.Parameters.AddWithValue("@played", played);
                                cmd.Parameters.AddWithValue("@Length", length);
                                cmd.Parameters.AddWithValue("@Url", urli);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
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
                    cmd.ExecuteNonQuery();
                }

            }
            int GetOrCreateUrlId(SQLiteConnection conn, string url)
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
                else return -1;
            }
        }

        private void InitDatabase()
        {
            string appDataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            string dbFullPath = System.IO.Path.Combine(appDataPath, "MusicBeeHistory.db");

            using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbFullPath};Version=3;")) {
            conn.Open();
            SQLiteCommand command = conn.CreateCommand();
            command.CommandText = @"PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
            command.CommandText = @"CREATE TABLE IF NOT EXISTS History (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Artist integer,
                    Album integer,
                    Title integer,
                    Genre integer,
                    player_state integer,
                    event_type integer,
                    Url integer,
                    Played float,
                    Lenght float,
                    Time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    foreign key(Artist) references Artists(Id),
                    foreign key(Album) references Albums(Id),
                    foreign key(Title) references Titles(Id),
                    foreign key(Genre) references Genres(Id),
                    foreign key(player_state) references player_states(Id),
                    foreign key(event_type) references event_types(Id),
                    foreign key(Url) references Urls(Id)
                )";
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
        }
    } }
}

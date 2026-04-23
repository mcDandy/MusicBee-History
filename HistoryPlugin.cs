using Microsoft.Data.Sqlite;
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private SqliteConnection conn;
        public void ReceiveNotification(NotificationType type)
        {
            if (new NotificationType[]{ NotificationType.PlayStateChanged, NotificationType.TrackChanged, NotificationType.TrackChanging}.Contains(type))
            {
                PlayState state = mbApiInterface.Player_GetPlayState();
                int played = mbApiInterface.Player_GetPosition();
                string artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
                string album = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);
                string title = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
                string genre = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Genre);

                if (new PlayState[]{PlayState.Stopped, PlayState.Paused, PlayState.Playing}.Contains(state))
                {
                    int? aid = GetOrCreateArtistId(conn, artist);
                    int? alid = GetOrCreateAlbumId(conn, album);
                    int? tid = GetOrCreateTitleId(conn, title);
                    int? gid = GetOrCreateGenreId(conn, genre);
                    CreateStateIfNotExists(conn, state);
                    CreateTypeIfNotExists(conn, type);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO History (Artist, Album, Title, Genre, Action, Played) VALUES (@artist, @album, @title, @genre, @action, @played);";
                        cmd.Parameters.AddWithValue("@artist", (object)aid ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@album", (object)alid ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@title", (object)tid ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@genre", (object)gid ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@action", (int)state);
                        cmd.Parameters.AddWithValue("@type", (int)type);
                        cmd.Parameters.AddWithValue("@played", played);

                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
        int? GetOrCreateArtistId(SqliteConnection conn, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                // Najdi existujícího interpreta
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id FROM Artists WHERE Value = @name";
                    cmd.Parameters.AddWithValue("@name", name);
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return Convert.ToInt32(result);
                }
                // Pokud neexistuje, vlož nového
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO Artists (Value) VALUES (@name); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@name", name);
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            } else return null;
        }
        int? GetOrCreateGenreId(SqliteConnection conn, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                // Najdi existující žánr
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id FROM Genres WHERE Value = @name";
                    cmd.Parameters.AddWithValue("@name", name);
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return Convert.ToInt32(result);
                }
                // Pokud neexistuje, vlož nový žánr
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO Genres (Value) VALUES (@name); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@name", name);
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            else return null;
        }
        int? GetOrCreateAlbumId(SqliteConnection conn, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                // Najdi existující album
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id FROM Albums WHERE Value = @name";
                    cmd.Parameters.AddWithValue("@name", name);
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return Convert.ToInt32(result);
                }
                // Pokud neexistuje, vlož nové album
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO Albums (Value) VALUES (@name); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@name", name);
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            } else return null;
        }
        int? GetOrCreateTitleId(SqliteConnection conn, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                // Najdi existující album
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id FROM Titles WHERE Value = @name";
                    cmd.Parameters.AddWithValue("@name", name);
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return Convert.ToInt32(result);
                }
                // Pokud neexistuje, vlož nové album
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO Titles (Value) VALUES (@name); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@name", name);
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            } else return null;
        }
        void CreateStateIfNotExists(SqliteConnection conn, PlayState state)
        {

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id FROM Actions WHERE Value = @name";
                    cmd.Parameters.AddWithValue("@name", state.ToString());
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return;
                }
                // Pokud neexistuje, vlož novou akci
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO Actions (Id,Value) VALUES (@id,@name);";
                    cmd.Parameters.AddWithValue("@id", (int)state);
                    cmd.Parameters.AddWithValue("@name", state.ToString());
                    cmd.ExecuteNonQuery();
                }

        }
        void CreateTypeIfNotExists(SqliteConnection conn, NotificationType state)
        {

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id FROM Types WHERE Value = @name";
                    cmd.Parameters.AddWithValue("@name", state.ToString());
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return;
                }
                // Pokud neexistuje, vlož novou akci
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO Types (Id,Value) VALUES (@id,@name);";
                    cmd.Parameters.AddWithValue("@id", (int)state);
                    cmd.Parameters.AddWithValue("@name", state.ToString());
                    cmd.ExecuteNonQuery();
                }

        }

        private void InitDatabase()
        {
            conn =new SqliteConnection("Data Source=MusicBeeHistory.db");

                conn.Open();
                SqliteCommand command = conn.CreateCommand();
                command.CommandText = @"PRAGMA foreign_keys = ON;";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS History (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Artist integer,
                    Album integer,
                    Title integer,
                    Genre integer,
                    State integer,
                    Type integer,
                    Played float,
                    Time TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    foreign key(Artist) references Artists(Id),
                    foreign key(Album) references Albums(Id),
                    foreign key(Title) references Titles(Id),
                    foreign key(Genre) references Genres(Id),
                    foreign key(State) references States(Id),
                    foreign key(Type) references Types(Id)
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
            command.CommandText = @"CREATE TABLE IF NOT EXISTS States (
                    Id INTEGER PRIMARY KEY,
                    Value TEXT UNIQUE
                )";
            command.ExecuteNonQuery();
            command.CommandText = @"CREATE TABLE IF NOT EXISTS Types (
                    Id INTEGER PRIMARY KEY,
                    Value TEXT UNIQUE
                )";
            command.ExecuteNonQuery();
        }
    }
}

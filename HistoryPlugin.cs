using Microsoft.Data.Sqlite;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        public void ReceiveNotification(NotificationType type)
        {
            if (type == NotificationType.PlayStateChanged)
            {
                var state = mbApiInterface.Player_GetPlayState();
                int played = mbApiInterface.Player_GetPosition();
                var artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
                var album = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);
                var title = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
                var genre = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Genre);

                if (state == PlayState.Stopped)
                {

                }
                else if (state == PlayState.Paused)
                {
                }
                else if (state == PlayState.Playing)
                {
                }
            }
            else if (type == NotificationType.TrackChanged)
            {

            }
            else if (type == NotificationType.TrackChanging)
            {

            }
        }
        private void InitDatabase()
        {
            using(var conn = new SqliteConnection("Data Source=MusicBeeHistory.db"))
{
                conn.Open();
                var command = conn.CreateCommand();
                command.CommandText = @"PRAGMA foreign_keys = ON;";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS History (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Artist integer,
                    Album integer,
                    Title integer,
                    Genre integer,
                    Action integer,
                    Played float,
                    Time TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    foreign key(Artist) references Artists(Id),
                    foreign key(Album) references Albums(Id),
                    foreign key(Title) references Titles(Id),
                    foreign key(Genre) references Genres(Id),
                    foreign key(Action) references Actions(Id)
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS Artists (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS Albums (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS Titles (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS Genres (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT UNIQUE
                )";
                command.ExecuteNonQuery();
            }

        }
    }
}

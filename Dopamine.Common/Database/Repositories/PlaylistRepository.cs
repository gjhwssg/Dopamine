﻿using Dopamine.Common.Database.Entities;
using Dopamine.Common.Database.Repositories.Interfaces;
using Dopamine.Common.Helpers;
using Digimezzo.Utilities.Log;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dopamine.Common.Database.Repositories
{
    public class PlaylistRepository : IPlaylistRepository
    {
        #region Variables
        private SQLiteConnectionFactory factory;
        #endregion

        #region Construction
        public PlaylistRepository()
        {
            this.factory = new SQLiteConnectionFactory();
        }
        #endregion

        #region IPlaylistRepository
        public async Task<List<Playlist>> GetPlaylistsAsync()
        {
            var selectedPlaylists = new List<Playlist>();

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.factory.GetConnection())
                    {
                        try
                        {
                            selectedPlaylists = conn.Table<Playlist>().Select((p) => p).ToList();
                            selectedPlaylists = selectedPlaylists.OrderBy((p) => Utils.GetSortableString(p.PlaylistName)).ToList();
                        }
                        catch (Exception ex)
                        {
                            LogClient.Error("Could not get the Playlists. Exception: {0}", ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not connect to the database. Exception: {0}", ex.Message);
                }
            });

            return selectedPlaylists;
        }

        public async Task<AddPlaylistResult> AddPlaylistAsync(string playlistName)
        {
            AddPlaylistResult result = AddPlaylistResult.Success;

            string trimmedPlaylistName = playlistName.Trim();

            if (string.IsNullOrEmpty(trimmedPlaylistName))
            {
                LogClient.Info("Could not add the Playlist because no playlist name was provided");
                return AddPlaylistResult.Blank;
            }

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.factory.GetConnection())
                    {
                        try
                        {
                            Playlist newPlaylist = new Playlist { PlaylistName = trimmedPlaylistName };
                            var existingPlaylistCount = conn.Query<Playlist>("SELECT * FROM Playlist WHERE TRIM(PlaylistName)=?", newPlaylist.PlaylistName).Count();

                            if (existingPlaylistCount == 0)
                            {
                                conn.Insert(newPlaylist);
                                LogClient.Info("Added the Playlist {0}", trimmedPlaylistName);
                            }
                            else
                            {
                                LogClient.Info("Didn't add the Playlist {0} because it is already in the database", trimmedPlaylistName);
                                result = AddPlaylistResult.Duplicate;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogClient.Error("Could not add the Playlist {0}. Exception: {1}", trimmedPlaylistName, ex.Message);
                            result = AddPlaylistResult.Error;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not connect to the database. Exception: {0}", ex.Message);
                }
            });

            return result;
        }

        public async Task<Playlist> GetPlaylistAsync(string playlistName)
        {
            Playlist dbPlaylist = null;

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.factory.GetConnection())
                    {
                        dbPlaylist = conn.Query<Playlist>("SELECT * FROM Playlist WHERE TRIM(PlaylistName)=?", playlistName.Trim()).FirstOrDefault();
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not connect to the database. Exception: {0}", ex.Message);
                }
            });

            return dbPlaylist;
        }

        public async Task<DeletePlaylistResult> DeletePlaylistsAsync(IList<Playlist> playlists)
        {
            DeletePlaylistResult result = DeletePlaylistResult.Success;

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.factory.GetConnection())
                    {
                        foreach (Playlist pl in playlists)
                        {
                            string trimmedPlaylistName = pl.PlaylistName.Trim();

                            // Get the Playlist in the database
                            Playlist dbPlaylist = conn.Query<Playlist>("SELECT * FROM Playlist WHERE TRIM(PlaylistName)=?", trimmedPlaylistName).FirstOrDefault();


                            if (dbPlaylist != null)
                            {
                                // Get the PlaylistEntries which contain the PlaylistID of the Playlist to delete
                                List<PlaylistEntry> playlistEntries = conn.Table<PlaylistEntry>().Where((p) => p.PlaylistID == dbPlaylist.PlaylistID).Select((p) => p).ToList();

                                conn.Delete(dbPlaylist);

                                foreach (var entry in playlistEntries)
                                {
                                    conn.Delete(entry);
                                }

                                LogClient.Info("Deleted the Playlist {0}", trimmedPlaylistName);
                            }
                            else
                            {
                                LogClient.Error("The playlist {0} could not be deleted because it was not found in the database.", trimmedPlaylistName);
                                result = DeletePlaylistResult.Error;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not connect to the database. Exception: {0}", ex.Message);
                }
            });

            return result;
        }

        public async Task<RenamePlaylistResult> RenamePlaylistAsync(string oldPlaylistName, string newPlaylistName)
        {
            RenamePlaylistResult result = RenamePlaylistResult.Success;

            string trimmedOldPlaylistName = oldPlaylistName.Trim();
            string trimmedNewPlaylistName = newPlaylistName.Trim();

            if (string.IsNullOrEmpty(trimmedNewPlaylistName))
            {
                LogClient.Info("Could not rename the Playlist {0} because no new playlist name was provided", trimmedOldPlaylistName);
                return RenamePlaylistResult.Blank;
            }

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.factory.GetConnection())
                    {
                        // Check if there is already a playlist with that name
                        var existingPlaylistCount = conn.Query<Playlist>("SELECT * FROM Playlist WHERE TRIM(PlaylistName)=?", trimmedNewPlaylistName).Count();

                        if (existingPlaylistCount == 0)
                        {
                            Playlist playlistToRename = conn.Query<Playlist>("SELECT * FROM Playlist WHERE TRIM(PlaylistName)=?", trimmedOldPlaylistName).ToList().FirstOrDefault();

                            if (playlistToRename != null)
                            {
                                playlistToRename.PlaylistName = trimmedNewPlaylistName;
                                conn.Update(playlistToRename);

                                result = RenamePlaylistResult.Success;
                            }
                            else
                            {
                                LogClient.Error("The playlist {0} could not be renamed because it was not found in the database.", trimmedOldPlaylistName);
                                result = RenamePlaylistResult.Error;
                            }
                        }
                        else
                        {
                            result = RenamePlaylistResult.Duplicate;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not connect to the database. Exception: {0}", ex.Message);
                }
            });

            return result;
        }

        public async Task<AddToPlaylistResult> AddTracksToPlaylistAsync(IList<MergedTrack> tracks, string playlistName)
        {
            var result = new AddToPlaylistResult { IsSuccess = true };
            int numberTracksAdded = 0;

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.factory.GetConnection())
                    {
                        try
                        {
                            // Get the PlaylistID of the Playlist with PlaylistName = iPlaylistName
                            var playlistID = conn.Table<Playlist>().Select((p) => p).Where((p) => p.PlaylistName.Equals(playlistName)).ToList().Select((p) => p.PlaylistID).FirstOrDefault();

                            // Loop over the Tracks in iTracks and add an entry to PlaylistEntries for each of the Tracks
                            foreach (MergedTrack t in tracks)
                            {
                                var possiblePlaylistEntry = new PlaylistEntry
                                {
                                    PlaylistID = playlistID,
                                    TrackID = t.TrackID
                                };

                                if (!conn.Table<PlaylistEntry>().ToList().Contains(possiblePlaylistEntry))
                                {
                                    conn.Insert(possiblePlaylistEntry);
                                    numberTracksAdded += 1;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogClient.Error("A problem occured while adding Tracks to Playlist with name '{0}'. Exception: {1}", playlistName, ex.Message);
                            result.IsSuccess = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not connect to the database. Exception: {0}", ex.Message);
                }
            });

            result.NumberTracksAdded = numberTracksAdded;

            return result;
        }

        

        

        public async Task<DeleteTracksFromPlaylistsResult> DeleteTracksFromPlaylistAsync(IList<MergedTrack> tracks, Playlist selectedPlaylist)
        {
            DeleteTracksFromPlaylistsResult result = DeleteTracksFromPlaylistsResult.Success;

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.factory.GetConnection())
                    {
                        if (tracks != null)
                        {
                            foreach (MergedTrack t in tracks)
                            {
                                try
                                {
                                    PlaylistEntry playlistEntryToDelete = conn.Table<PlaylistEntry>().Select((p) => p).Where((p) => p.TrackID.Equals(t.TrackID) & p.PlaylistID.Equals(selectedPlaylist.PlaylistID)).FirstOrDefault();
                                    conn.Delete(playlistEntryToDelete);
                                }
                                catch (Exception ex)
                                {
                                    LogClient.Error("An error occured while deleting PlaylistEntry for Track '{0}'. Exception: {1}", t.Path, ex.Message);
                                    result = DeleteTracksFromPlaylistsResult.Error;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not connect to the database. Exception: {0}", ex.Message);
                }
            });

            return result;
        }

        public async Task<string> GetUniquePlaylistNameAsync(string name)
        {
            string uniqueName = name;

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.factory.GetConnection())
                    {
                        int number = 1;

                        while (conn.Table<Playlist>().Select((p) => p).ToList().Select((p) => p.PlaylistName).ToList().Contains(uniqueName))
                        {
                            number += 1;
                            uniqueName = name + " " + number;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not connect to the database. Exception: {0}", ex.Message);
                }
            });

            return uniqueName;
        }

        #endregion
    }
}

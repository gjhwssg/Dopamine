﻿using Digimezzo.Utilities.Utils;
using Dopamine.Common.Services.Cache;
using Dopamine.Common.Services.Metadata;
using Dopamine.Common.Services.Playback;
using Dopamine.Common.Base;
using Dopamine.Common.Database;
using Dopamine.Common.Database.Entities;
using Dopamine.Common.Extensions;
using Dopamine.Common.IO;
using Digimezzo.Utilities.Log;
using Dopamine.Common.Metadata;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;

namespace Dopamine.Common.Services.File
{
    public class FileService : IFileService
    {
        #region Variables
        private IPlaybackService playbackService;
        private ICacheService cacheService;
        private IMetadataService metadataService;
        private IList<string> files;
        private object lockObject = new object();
        private Timer addFilesTimer;
        private int addFilesTimeout = 250; // milliseconds
        private string instanceGuid;
        #endregion

        #region Construction
        public FileService(IPlaybackService playbackService, ICacheService cacheService, IMetadataService metadataService)
        {
            this.playbackService = playbackService;
            this.cacheService = cacheService;
            this.metadataService = metadataService;

            // Unique identifier which will be used by this instance only to create cached artwork.
            // This prevents the cleanup function to delete artwork which is in use by this instance.
            this.instanceGuid = Guid.NewGuid().ToString();

            this.files = new List<string>();
            this.addFilesTimer = new Timer();
            this.addFilesTimer.Interval = this.addFilesTimeout;
            this.addFilesTimer.Elapsed += AddFilesTimerElapsedHandler;
            this.DeleteFileArtworkFromCacheAsync(this.instanceGuid);
        }
        #endregion

        #region IFileService
        public void ProcessArguments(string[] args)
        {
            this.ProcessArgumentsAsync(args);
        }
        #endregion

        #region Private
        private async Task ProcessArgumentsAsync(string[] args)
        {
            if (args.Length > 1)
            {
                this.addFilesTimer.Stop();

                await Task.Run(() =>
                {
                    LogClient.Info("Received commandline arguments.");

                    // Don't process index=0, as this contains the name of the executable.
                    for (int index = 1; index <= args.Length - 1; index++)
                    {
                        lock (this.lockObject)
                        {
                            this.files.Add(args[index]);
                            LogClient.Info("Added file '{0}'", args[index]);
                        }
                    }
                });

                this.RestartAddFilesTimer();
            }
        }

        private void RestartAddFilesTimer()
        {
            this.addFilesTimer.Stop();
            this.addFilesTimer.Start();
        }

        private async void AddFilesTimerElapsedHandler(Object sender, ElapsedEventArgs e)
        {
            this.addFilesTimer.Stop();

            // Check if there is only 1 instance (this one) of the application running. If not,
            // that could mean there are other instances trying to send files to this instance.
            if (EnvironmentUtils.IsSingleInstance(ProductInformation.ApplicationAssemblyName))
            {
                lock (this.lockObject)
                {
                    LogClient.Info("Finished adding files. Number of files added = {0}", this.files.Count.ToString());
                }

                await Application.Current.Dispatcher.BeginInvoke(new Action(async () => await this.ImportFilesAsync()));
            }
            else
            {
                // There are still other instances trying to send files. Check again next time.
                this.RestartAddFilesTimer();
            }
        }

        private async Task ImportFilesAsync()
        {
            var tracks = new List<MergedTrack>();

            await Task.Run(async () =>
            {
                List<string> tempFiles = null;

                lock (this.lockObject)
                {
                    tempFiles = (List<string>)this.files.Clone();
                    this.files.Clear(); // Clear the list
                }

                tempFiles.Sort(); // Sort the files alphabetically

                if (tempFiles == null) return;

                // Convert the files to tracks
                foreach (string path in tempFiles)
                {
                    if (FileFormats.IsSupportedAudioFile(path))
                    {
                        // The file is a supported audio format: add it directly.
                        tracks.Add(await this.Path2TrackAsync(path, "file-" + this.instanceGuid));

                    }
                    else if (FileFormats.IsSupportedPlaylistFile(path))
                    {
                        // The file is a supported playlist format: process the contents of the playlist file.
                        List<string> audioFilePaths = this.ProcessPlaylistFile(path);

                        foreach (string audioFilePath in audioFilePaths)
                        {
                            tracks.Add(await this.Path2TrackAsync(audioFilePath, "file-" + this.instanceGuid));
                        }
                    }
                    else if (Directory.Exists(path))
                    {
                        // The file is a directory: get the audio files in that directory.
                        List<string> audioFilePaths = this.ProcessDirectory(path);

                        foreach (string audioFilePath in audioFilePaths)
                        {
                            tracks.Add(await this.Path2TrackAsync(audioFilePath, "file-" + this.instanceGuid));
                        }
                    }
                    else
                    {
                        // The file is unknown: do not process it.
                    }
                }
            });

            LogClient.Info("Number of tracks to play = {0}", tracks.Count.ToString());

            if (tracks.Count > 0)
            {
                LogClient.Info("Enqueuing {0} tracks.", tracks.Count.ToString());
                await this.playbackService.Enqueue(tracks);
            }
        }

        private List<string> ProcessPlaylistFile(string playlistPath)
        {
            var decoder = new PlaylistDecoder();
            DecodePlaylistResult decodeResult = decoder.DecodePlaylist(playlistPath);

            if (!decodeResult.DecodeResult.Result)
            {
                LogClient.Error("Error while decoding playlist file. Exception: {0}", decodeResult.DecodeResult.GetMessages());
            }

            return decodeResult.Paths;
        }

        private List<string> ProcessDirectory(string directoryPath)
        {
            List<string> paths = new List<string>();

            // Create a queue to hold exceptions that have occurred while scanning the directory tree
            var recurseExceptions = new ConcurrentQueue<Exception>();
            FileOperations.TryDirectoryRecursiveGetFiles(directoryPath, paths, FileFormats.SupportedMediaExtensions, recurseExceptions);

            if (recurseExceptions.Count > 0)
            {
                foreach (Exception recurseException in recurseExceptions)
                {
                    LogClient.Error("Error while recursively getting files/folders. Exception: {0}", recurseException.ToString());
                }
            }

            return paths;
        }

        private async Task DeleteFileArtworkFromCacheAsync(string exclude)
        {
            await Task.Run(() =>
            {
                string[] artworkFiles = null;

                try
                {
                    if (System.IO.Directory.Exists(this.cacheService.CoverArtCacheFolderPath))
                    {
                        artworkFiles = System.IO.Directory.GetFiles(this.cacheService.CoverArtCacheFolderPath, "file-*.jpg");
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("There was a problem while fetching file artwork. Exception: {0}", ex.Message);
                }

                if (artworkFiles != null && artworkFiles.Count() > 0)
                {

                    foreach (string artworkFile in artworkFiles)
                    {
                        try
                        {
                            // Do not delete file from this instance
                            if (!artworkFile.StartsWith("file-" + this.instanceGuid))
                            {
                                System.IO.File.Delete(artworkFile);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogClient.Error("There was a problem while deleting cached file artwork {0}. Exception: {1}", artworkFile, ex.Message);
                        }
                    }
                }
            });
        }

        public async Task<MergedTrack> Path2TrackAsync(string path, string artworkPrefix)
        {
            var mergedTrack = new MergedTrack();

            await Task.Run(() =>
            {
                var track = new Track();
                var album = new Album();
                var artist = new Artist();
                var genre = new Genre();

                try
                {
                    MetadataUtils.SplitMetadata(path, ref track, ref album, ref artist, ref genre);

                    mergedTrack.Path = track.Path;
                    mergedTrack.SafePath = track.Path.ToSafePath();
                    mergedTrack.FileName = track.FileName;
                    mergedTrack.MimeType = track.MimeType;
                    mergedTrack.FileSize = track.FileSize;
                    mergedTrack.BitRate = track.BitRate;
                    mergedTrack.SampleRate = track.SampleRate;
                    mergedTrack.TrackTitle = track.TrackTitle;
                    mergedTrack.TrackNumber = track.TrackNumber;
                    mergedTrack.TrackCount = track.TrackCount;
                    mergedTrack.DiscNumber = track.DiscNumber;
                    mergedTrack.DiscCount = track.DiscCount;
                    mergedTrack.Duration = track.Duration;
                    mergedTrack.Year = track.Year;
                    mergedTrack.Rating = track.Rating;
                    mergedTrack.HasLyrics = track.HasLyrics;

                    mergedTrack.ArtistName = artist.ArtistName;

                    mergedTrack.GenreName = genre.GenreName;

                    mergedTrack.AlbumTitle = album.AlbumTitle;
                    mergedTrack.AlbumArtist = album.AlbumArtist;
                    mergedTrack.AlbumYear = album.Year;
                }
                catch (Exception ex)
                {
                    // Make sure the file can be opened by creating a Track with some default values
                    mergedTrack = MergedTrack.CreateDefault(path);
                    LogClient.Error("Error while creating Track from file '{0}'. Creating default track. Exception: {1}", path, ex.Message);
                }
            });

            return mergedTrack;
        }
        #endregion
    }
}

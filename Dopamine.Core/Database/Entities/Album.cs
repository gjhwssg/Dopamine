﻿using System.Collections.Generic;
using SQLite;

namespace Dopamine.Core.Database.Entities
{
    public class Album
    {
        #region Properties
        [PrimaryKey()]
        public long AlbumID { get; set; }
        public string AlbumTitle { get; set; }
        public string AlbumArtist { get; set; }
        public long? Year { get; set; }
        public string ArtworkID { get; set; }
        public long DateLastSynced { get; set; }
        public long DateAdded { get; set; }
        #endregion

        #region ReadOnly Properties
        [Ignore()]
        public string AlbumTitleTrim
        {
            get { return AlbumTitle.Trim(); }
        }

        [Ignore()]
        public string AlbumArtistTrim
        {
            get { return AlbumArtist.Trim(); }
        }
        #endregion

        #region Overrides
        public override bool Equals(object obj)
        {
            if (obj == null || !GetType().Equals(obj.GetType()))
            {
                return false;
            }

            return this.AlbumTitleTrim.Equals(((Album)obj).AlbumTitleTrim) & this.AlbumArtistTrim.Equals(((Album)obj).AlbumArtistTrim);
        }

        public override int GetHashCode()
        {
            return new { this.AlbumTitleTrim, this.AlbumArtistTrim }.GetHashCode();
        }
        #endregion

        #region Public
        public IList<Album> ToList()
        {
            List<Album> l = new List<Album>();
            l.Add(this);

            return l;
        }
        #endregion
    }
}

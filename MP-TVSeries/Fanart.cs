#region GNU license
// MP-TVSeries - Plugin for Mediaportal
// http://www.team-mediaportal.com
// Copyright (C) 2006-2007
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
//
//+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
//+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;

namespace WindowPlugins.GUITVSeries
{
    public class Fanart : IDisposable
    {
        #region config Constants
        
        const string seriesFanArtFilenameFormat = "*{0}*.*";
        const string seasonFanArtFilenameFormat = "*{0}S{1}*.*";
        const string lightIdentifier = "_light_";
        
        #endregion

        #region Static Vars

        static Dictionary<int, Fanart> fanartsCache = new Dictionary<int, Fanart>();
        static Random fanartRandom = new Random();
        static Object lockObject = new Object();

        #endregion

        #region Vars

        int _seriesID = -1;
        int _seasonIndex = -1;

        bool? _isLight = null;
        bool _seasonMode = false;

        List<string> _fanArts = null;

        string _randomPick = null;        

        DBFanart _dbchosenfanart = null;

        #endregion

        #region Properties
        
        public int SeriesID 
        { 
            get { return _seriesID; } 
        }

        public int SeasonIndex 
        { 
            get { return _seasonIndex; }
        }

        public bool Found 
        { 
            get
            { 
                return _fanArts != null && _fanArts.Count > 0; 
            } 
        }
        
        public bool SeasonMode 
        { 
            get { return _seasonMode; } 
        }

        public bool HasColorInfo
        {
            get 
            {
                return _dbchosenfanart != null && _dbchosenfanart.HasColorInfo;
            }
        }

        public System.Drawing.Color[] Colors
        {
            get
            {
                if (HasColorInfo)
                {
                    System.Drawing.Color[] colors = new System.Drawing.Color[3];
                    for (int i = 0; i < 3;)
                        colors[i] = _dbchosenfanart.GetColor(++i);
                    return colors;
                }
                else return null;
            }
        }

        public static string RGBColorToHex(System.Drawing.Color color)
        {
            // without alpha
            return String.Format("{0:x}", color.R) +
                   String.Format("{0:x}", color.G) +
                   String.Format("{0:x}", color.B);
        }

        #endregion

        #region Private Constructors

        Fanart(int seriesID)
        {
            _seriesID = seriesID;
            _seasonMode = false;
            getFanart();
        }

        Fanart(int seriesID, int seasonIndex)
        {
            _seriesID = seriesID;
            _seasonIndex = seasonIndex;
            _seasonMode = true;
            getFanart();
        }

        #endregion

        #region Static Methods
       
        public static Fanart getFanart(int seriesID)
        {
            // possibly multiple plugins accessing fanart at the same time
            lock (lockObject)
            {
                Fanart f = null;
                if (fanartsCache.ContainsKey(seriesID))
                {
                    f = fanartsCache[seriesID];
                    f.ForceNewPick();
                }
                else
                {
                    // this will get simple drop-ins (old method)
                    f = new Fanart(seriesID); 
                    fanartsCache.Add(seriesID, f);
                }
                return f;
            }
        }

        public static bool RefreshFanart(int seriesID)
        {
            Fanart f = null;
            if (fanartsCache.ContainsKey(seriesID))
            {
                f = fanartsCache[seriesID];
                f.getFanart();
                return true;
            }
            else
                return false;
        }

        public static Fanart getFanart(int seriesID, int seasonIndex)
        {
            // no cache for now for series
            return new Fanart(seriesID, seasonIndex);
        }

        /// <summary>
        /// Deletes all cached thumbs for a Series Fanart
        /// Sometimes fanarts get deleted online but we still have the old
        /// thumbs cached. This allows the user to clear the cache and re-download the thumbs
        /// </summary>
        /// <param name="seriesID"></param>
        public static void ClearFanartCache(int seriesID) {                    
            string searchPattern = seriesID.ToString() + "*.jpg";
            string cachePath = Helper.PathCombine(Settings.GetPath(Settings.Path.fanart), @"_cache\fanart\original\");
            if (!System.IO.Directory.Exists(cachePath)) return; //exit if no dir to avoid any exception in GetFiles
            string[] fileList = Directory.GetFiles(cachePath, searchPattern);

            foreach (string file in fileList) {
                MPTVSeriesLog.Write("Deleting Cached Fanart Thumbnail: " + file);
                FileInfo fileInfo = new FileInfo(file);                
                try {
                    fileInfo.Delete();
                }
                catch (Exception ex) {
                    MPTVSeriesLog.Write("Failed to Delete Cached Fanart Thumbnail: " + file + ": " + ex.Message);
                }
            }

            // Clear DB and Clear Cache
            DBFanart.ClearDB(seriesID);
        }

        #endregion

        #region Instance Methods

        public string FanartThumbFilename
        {
            get
            {
                if (!string.IsNullOrEmpty(_ThumbFileName)) return _ThumbFileName;

                List<DBFanart> fanarts = DBFanart.GetAll(SeriesID, true);

                // check if we have populated the db with fanarts
                if (fanarts == null || fanarts.Count == 0) 
                    return string.Empty;

                // get a fallback fanart if preferred one does not exist
                _ThumbFileName = Path.Combine(Settings.GetPath(Settings.Path.fanart), fanarts[0][DBFanart.cThumbnailPath]);
                
                // favour chosen fanart if it exists
                fanarts.RemoveAll(f => f.Chosen != true);

                // should only be left with one if there is one chosen
                if (fanarts.Count > 0)
                    _ThumbFileName = Path.Combine(Settings.GetPath(Settings.Path.fanart), fanarts[0][DBFanart.cThumbnailPath]);

                // cached thumbs may not be downloaded
                // currently only get retrieved on fanart chooser window open
                if (File.Exists(_ThumbFileName))
                    return _ThumbFileName;
                else
                {
                    // lets create one from the fullsize fanart on disk
                    string fullSizeFanart = Fanart.getFanart(SeriesID).FanartFilename;
                    if (!string.IsNullOrEmpty(fullSizeFanart) && File.Exists(fullSizeFanart) &&
                        _ThumbFileName.Length > Settings.GetPath(Settings.Path.fanart).Length) // only if we have filename
                    {
                        try
                        {
                            //create directory first, to get rid of GDI+ errors
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_ThumbFileName));

                            Image img = Image.FromFile(fullSizeFanart);
                            Bitmap bmp = ImageAllocator.Resize(img, new Size(400, 225));

                            bmp.Save(_ThumbFileName, ImageFormat.Jpeg);
                            return _ThumbFileName;
                        }
                        catch (Exception ex)
                        {
                            MPTVSeriesLog.Write("Failed to create fanart thumbnail {0} from full fanart {1}: {2}",  _ThumbFileName, fullSizeFanart, ex.ToString());
                        }
                    }
                }

                // let skin handle it
                return string.Empty;
            }
        } string _ThumbFileName = string.Empty;

        public string FanartFilename
        {
            get 
            {
                // Maybe there has been some new additions we dont know about yet
                if (_fanArts != null && _fanArts.Count == 0)
                {
                    getFanart();
                    if (_fanArts.Count == 0)
                        return string.Empty;
                }
                
                if (DBOption.GetOptions(DBOption.cFanartRandom))
                {                                        
                    if (_randomPick != null && _randomPick != String.Empty)
                        return _randomPick;

                    List<DBFanart> _faInDB = null;

                    if (DBFanart.GetAll(SeriesID, true) != null && (_faInDB = DBFanart.GetAll(SeriesID, true)) != null && _faInDB.Count > 0)
                    {
                        // Choose from db takes precedence (not ideal)                        
                        List<DBFanart> _tempFanarts = _faInDB;
                        for (int i = (_tempFanarts.Count - 1); i >= 0; i--)                        
                        {
                            // Remove any fanarts in database that are not local or have been disabled
                            if (!_tempFanarts[i].isAvailableLocally || _tempFanarts[i].Disabled)
                                _faInDB.Remove(_faInDB[i]);
                        }
                        // we may no longer have any fanarts in db to choose from as they could be disabled/deleted from disk
                        if (_faInDB.Count > 0)
                            _randomPick = _faInDB[fanartRandom.Next(0, _faInDB.Count)].FullLocalPath;

                        if (String.IsNullOrEmpty(_randomPick))
                        {
                            if (_fanArts != null && _fanArts.Count > 0)
                                _randomPick = _fanArts[fanartRandom.Next(0, _fanArts.Count)];
                            else
                                _randomPick = string.Empty;
                        }
                    }
                    else
                    {
                        if (_fanArts != null && _fanArts.Count > 0)
                            _randomPick = _fanArts[fanartRandom.Next(0, _fanArts.Count)];
                        else
                            _randomPick = string.Empty;
                    }
                    return _randomPick;
                }
                else
                {                    
                    // see if we have a chosen one in the db
                    List<DBFanart> _faInDB = DBFanart.GetAll(SeriesID, true);
                    if (_faInDB != null && _faInDB.Count > 0)
                    {
                        foreach (DBFanart f in _faInDB)
                        {
                            if (f.Chosen && f.isAvailableLocally && !f.Disabled)
                            {
                                _dbchosenfanart = f;
                                break;
                            }
                        }

                        // we couldnt find any fanart set as chosen in db, we try to choose the first available
                        if (_dbchosenfanart == null || String.IsNullOrEmpty(_dbchosenfanart.FullLocalPath))
                        {
                            foreach (DBFanart f in _faInDB)
                            {
                                // Checking if available will also remove from database if not
                                if (f.isAvailableLocally && !f.Disabled)
                                {
                                    _dbchosenfanart = f;
                                    break;
                                }
                            }
                        }

                        if (_dbchosenfanart != null)
                            return _dbchosenfanart.FullLocalPath;

                        // If still no fanart found in db, choose from available on harddrive
                        if (_dbchosenfanart == null || String.IsNullOrEmpty(_dbchosenfanart.FullLocalPath))
                        {
                            if (_fanArts != null && _fanArts.Count > 0)
                                return _randomPick = _fanArts[0];
                            else
                                return string.Empty;
                        }
                        else
                            return _dbchosenfanart.FullLocalPath;
                    }
                    else
                    {
                        // No fanart found in db but user doesn't want random, we always return the first
                        if (_fanArts != null && _fanArts.Count > 0)
                            return _randomPick = _fanArts[0];
                        else 
                            return string.Empty;
                    }
                }
            }
        }

        public bool RandomPickIsLight
        {
            get
            {
                if (_isLight != null) return _isLight.Value;
                _isLight = fanArtIsLight(FanartFilename);
                return _isLight.Value;
            }
        }

        public void ForceNewPick()
        {
            _isLight = null;
            _randomPick = null;
        }

        #endregion

        #region Implementation Instance Methods

        bool fanArtIsLight(string fanartFilename)
        {
            return (fanartFilename.Contains(lightIdentifier));
        }

        /// <summary>
        /// Creates a List of Fanarts in your Thumbs folder
        /// </summary>
        void getFanart()
        {            
            string fanartFolder = Settings.GetPath(Settings.Path.fanart);

            // Check if Fanart folder exists in MediaPortal's Thumbs directory
            if (System.IO.Directory.Exists(fanartFolder))
            {
                MPTVSeriesLog.Write("Checking for Fanart on series: ", Helper.getCorrespondingSeries(_seriesID).ToString(), MPTVSeriesLog.LogLevel.Debug);
                try
                {
                    // Create a Filename filter for Season / Series Fanart
                    string seasonFilter = string.Format(seasonFanArtFilenameFormat, _seriesID, _seasonIndex);
                    string seriesFilter = string.Format(seriesFanArtFilenameFormat, _seriesID);
                    
                    string filter = _seasonMode ? seasonFilter : seriesFilter;                                  

                    _fanArts = new List<string>();
                    // Store list of all fanart files found in all sub-directories of fanart thumbs folder
                    _fanArts.AddRange(System.IO.Directory.GetFiles(fanartFolder, filter, System.IO.SearchOption.AllDirectories));

                    // If no Season Fanart was found, see if any Series fanart exists
                    if (_fanArts.Count == 0 && _seasonMode)
                    {
                        MPTVSeriesLog.Write("No Season Fanart found on disk, searching for series fanart", MPTVSeriesLog.LogLevel.Debug);
                        _fanArts.AddRange(System.IO.Directory.GetFiles(fanartFolder, seriesFilter, System.IO.SearchOption.AllDirectories));
                    }

                    // Remove any files that we dont want e.g. thumbnails in the _cache folder
                    // and Season fanart if we are not in Season Mode
                    if (!_seasonMode) removeSeasonFromSeries();
                    removeFromFanart("_cache");

                    MPTVSeriesLog.Write("Number of fanart found on disk: ", _fanArts.Count.ToString(), MPTVSeriesLog.LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    MPTVSeriesLog.Write("An error occured looking for fanart: " + ex.Message);
                }
            }
        }

        void removeSeasonFromSeries()
        {
            string seasonFormat = SeriesID.ToString() + 'S';
            removeFromFanart(seasonFormat);
        }

        void removeFromFanart(string needle)
        {
            if (_fanArts == null) return;
            for (int i = 0; i < _fanArts.Count; i++)
            {
                if (_fanArts[i].Contains(needle))
                {
                    _fanArts.Remove(_fanArts[i]);
                    i--;
                }
            }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion
    }
}

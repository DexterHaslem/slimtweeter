using slimTweet.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net.Cache;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Serialization;
using Tweetinvi.Core.Interfaces;

namespace slimTweet
{
    //tweetsharp already has tons of models that are already notifypropertychanged
    // hammock.Model provides PropertyChangedBase, but its not the nice callermembername version
    public class PropertyChangedBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string name = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    public class NotBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ImageUrlSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var tweet = value as Tweet;
            if (tweet == null || string.IsNullOrEmpty(tweet.ProfileImageUrl))
                return null;
            return StorageManager.LoadUrl(tweet.ProfileImageUrl);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class RelativeTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            // current api used returns PC time not UTC derp
            var absTime = (DateTime)value;
            var diff = DateTime.Now - absTime;
            if (diff.TotalHours >= 1)
                return string.Format("{0} hour{2} {1} min", (int)diff.TotalHours, (int)diff.TotalMinutes % 60, (int)diff.TotalHours > 1 ? "s" : string.Empty);
            // seeing seconds update in real time is cool but jarring
            //return string.Format("{0} min{2} {1} seconds ago", (int)diff.TotalMinutes, (int)diff.TotalSeconds % 60, (int)diff.TotalMinutes > 1 ? "s" : "");
            var minRounded = (int)Math.Round(diff.TotalMinutes);
            return minRounded < 1 ? string.Format("{0} sec", Math.Round(diff.TotalSeconds)) : string.Format("{0} min{1}", minRounded, minRounded > 1 ? "s" : string.Empty);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
namespace slimTweet.Models
{
    internal class MessageConversation
    {
        public IUser From { get; set; }

        public ObservableCollection<IMessage> ReceivedMessages      { get; private set; }
        public ObservableCollection<IMessage> SentMessages          { get; private set; }
        public ObservableCollection<IMessage> MessagesByTime        { get; private set; }

        public static IEnumerable<MessageConversation> ParseConversations(IEnumerable<IMessage> received, IEnumerable<IMessage> sent)
        {
            return new MessageConversation[0];
        }
    }

    // library agnostic wrapper for everything we need
    public class Tweet : PropertyChangedBase
    {
        private DateTime serializeDateTimeStartDelta = new DateTime(2015, 05, 21);

        public long     Id                  { get; set; }
        public long     CreatorId           { get; set; }
        public int      FavoriteCount       { get; set; }
        public bool     IsFavorited         { get; set; }
        public bool     IsRetweeted         { get; set; }

        public string   ProfileImageUrl     { get; set; }
        public string   Name                { get; set; }
        public string   ScreenName          { get; set; }
        public int      RetweetCount        { get; set; }
        public string   Text                { get; set; }

        public ObservableCollection<Tweet> Children { get; set; }

        [XmlIgnore]
        public DateTime CreatedAt           { get; set; }

        [Browsable(false)]
        public long CreatedAtSerialize
        {
            get { return  CreatedAt.Ticks - serializeDateTimeStartDelta.Ticks; }
            set 
            {
                CreatedAt = new DateTime(serializeDateTimeStartDelta.Ticks + value);
            }
        }

        public Tweet()
        {
            Children = new ObservableCollection<Tweet>();
        }

        public static Tweet FromApi(ITweet apiTweet)
        {
            return new Tweet
            {
                Id              = apiTweet.Id,
                CreatorId       = apiTweet.Creator.Id,
                ProfileImageUrl = apiTweet.Creator.ProfileImageUrl,
                Name            = apiTweet.Creator.Name,
                ScreenName      = "@" + apiTweet.Creator.ScreenName, // api doesnt do this
                Text            = apiTweet.Text,
                CreatedAt       = apiTweet.CreatedAt,
                IsFavorited     = apiTweet.Favourited,
                IsRetweeted     = apiTweet.IsRetweet,
                RetweetCount    = apiTweet.RetweetCount,
                FavoriteCount   = apiTweet.FavouriteCount,
            };
        }

        public void InvalidateTimestamp()
        {
            // force binding update
            // ReSharper disable once ExplicitCallerInfoArgument
            OnPropertyChanged("CreatedAt");
        }
    }

    internal class TwitterAccountAuth// : PropertyChangedBase
    {
        internal const string ConsumerKey       = "8OFU8Km0dHIuyZeKLfcKJfVir";
        internal const string ConsumerSecret    = "TC8djXiJR2q0CT1lfxK7KuD4XrCTA1Rd9E89JLWRCAxJwurLtK";

        private const string _delim = "|";

        public string   Token           { get; set; }
        public string   TokenSecret     { get; set; }
        public string   Verification    { get; set; }

        public override string ToString()
        {
            return string.Format("{0}{3}{1}{3}{2}", Verification, Token, TokenSecret, _delim);
        }

        public static TwitterAccountAuth FromString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;
            var chunks = input.Split(new[] { _delim }, StringSplitOptions.RemoveEmptyEntries);
            // beware: we read in the newline, split is on \n only i guess
            return chunks.Length != 3 ? null : new TwitterAccountAuth { Verification = chunks[0], Token = chunks[1], TokenSecret = chunks[2].TrimEnd('\r', '\n') };
        }
    }

    internal class StorageManager
    {
        private const string _userDbFileName = "user.db";
        private const string _tweetsDbFileName = "tw.db";
#if MULTI_ACCT
        private const string _multiDbFileName = "users.db";
        public void SaveToIsolatedStorage(IEnumerable<TwitterAccountAuth> accts)
        {
            // simple squished text format
            //var isoStream = isolatedStorageFile.CreateFile("users.db"); this one sucks
            using (var isoStream = new IsolatedStorageFileStream("users.db", FileMode.Create))
                using (var streamWriter = new StreamWriter(isoStream))
                    foreach (var acct in accts)
                        streamWriter.WriteLine(acct.ToString());
        }

        public IEnumerable<TwitterAccountAuth> Restore()
        {
            try 
            {
                var isoStream = new IsolatedStorageFileStream("users.db", FileMode.Open);
                using (var streamReader = new StreamReader(isoStream))
                    return streamReader.ReadToEnd().Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(TwitterAccountAuth.FromString).Where(attemptNew => attemptNew != null).ToList();
            }
            catch (FileNotFoundException)
            {
                return Enumerable.Empty<TwitterAccountAuth>();
            }
        }
#endif

        public static void SaveAccount(TwitterAccountAuth acct)
        {
            using (var isoStream = new IsolatedStorageFileStream(_userDbFileName, FileMode.Create))
                using (var streamWriter = new StreamWriter(isoStream))
                    streamWriter.WriteLine(acct.ToString());
        }

        public static TwitterAccountAuth RestoreAccount()
        {
            try
            {
                var isoStream = new IsolatedStorageFileStream(_userDbFileName, FileMode.Open);
                using (var streamReader = new StreamReader(isoStream))
                    return TwitterAccountAuth.FromString(streamReader.ReadToEnd());
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        public static void SaveTweets(IEnumerable<Tweet> tweets2Save)
        {
            using (var isoStream = new IsolatedStorageFileStream(_tweetsDbFileName, FileMode.Create))
                using (var streamWriter = new StreamWriter(isoStream))
                {
                    var xmlSerializer = new XmlSerializer(typeof(Tweet[]));
                    var tweetArray = tweets2Save.ToArray();
                    xmlSerializer.Serialize(streamWriter, tweetArray);
                }
        }

        public static IEnumerable<Tweet> RestoreTweets()
        {
            try
            {
                var isoStream = new IsolatedStorageFileStream(_tweetsDbFileName, FileMode.Open);
                using (var streamReader = new StreamReader(isoStream))
                {
                    var xmlSerializer = new XmlSerializer(typeof(Tweet[]));
                    var deserialized = (Tweet[]) xmlSerializer.Deserialize(streamReader);

                    // just incase, prune duplicates..
                    var final = new List<Tweet>();
                    var seenIds = new HashSet<long>();
                    foreach (var nt in deserialized.Where(nt => !seenIds.Contains(nt.Id)))
                    {
                        seenIds.Add(nt.Id);
                        final.Add(nt);
                    }
                    return final;
                }
            }
            catch (FileNotFoundException)
            {
                return Enumerable.Empty<Tweet>();
            }
        }

        public static BitmapImage LoadUrl(string url)
        {
            //Uri uri = new Uri(url);
            //if (uri.IsFile)
            //{
            //    string fileName = Path.GetFileName(uri.LocalPath);
            //    IsolatedStorageFileStream fileStream = null;
            //    // see if we already cached in our isolated storage
            //    try 
            //    {
            //        fileStream = new IsolatedStorageFileStream(fileName, FileMode.Open);
            //    }
            //    catch
            //    {
            //        // download the image first
            //        fileStream = new IsolatedStorageFileStream(fileName, FileMode.Create);
            //        new System.Net.WebClient().DownloadFile(uri, fileName);
            //    }

            //    var imgFromStream = new BitmapImage();
            //    imgFromStream.BeginInit();
            //    imgFromStream.StreamSource = fileStream;
            //    imgFromStream.EndInit();
            //    return imgFromStream;
            //}
            var newImg = new BitmapImage(new Uri(url), new HttpRequestCachePolicy(HttpCacheAgeControl.MaxAge, TimeSpan.FromDays(1)));
            RenderOptions.SetBitmapScalingMode(newImg, BitmapScalingMode.HighQuality);
            return newImg;
        }
    }
}

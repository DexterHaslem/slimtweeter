//#define OFFLINE //uncomment this to only show persisted tweets (good for not hitting rate limit during dbugging)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using slimTweet.Models;
using Tweetinvi;
using Tweetinvi.Core.Interfaces;
using Tweetinvi.Core.Interfaces.Credentials;
using Tweetinvi.Core.Interfaces.Streaminvi;
using Tweetinvi.Logic.Model.Parameters;
using Tweet = slimTweet.Models.Tweet;

namespace slimTweet
{
    internal class ViewModel : PropertyChangedBase
    {
        private TwitterAccountAuth          _accountAuth;
        private const int                   _maxTweetBackfill               = 500;
        private const int                   _maxMessages                    = 25;
        private readonly MainView           _owner;
        private bool                        _isAddNewAccountVisible;
        private bool                        _isAuthorizeNewAccountVisible;
        private bool                        _isAuthInProgress;
        private const int                   _numTweetsToPersist             = 50000;
        private string                      _verificationText;
        private readonly DispatcherTimer    _updateTimer;
        private const int                   _updateRateSeconds              = 30;
        private ITemporaryCredentials       _tmpAppCredentials;
        BackgroundWorker _streamWorker;
        private IUserStream _userStream;
        private ITweetStream _tweetStream;


        public static ICommand AddNewAccountCommand         = new RoutedCommand("AddNewAccount", typeof(ViewModel));
        public static ICommand AuthorizeNewAccountCommand   = new RoutedCommand("AuthorizeNewAccount", typeof(ViewModel));
        // TODO: this isnt wired up i fell asleep
        public static ICommand NewTweetCommand              = new RoutedCommand("NewTweetCommand", typeof(ViewModel));

        //ITweet versus TwitterStatus for shitty fucking libraries that all rely on reflection heavy JSON library that blows itself up ftw
        public ObservableCollection<Tweet>                  TimelineFeed                { get; set; }
        public ObservableCollection<IMention>               MentionsFeed                { get; set; }
        public ObservableCollection<MessageConversation>    MessageConversations        { get; set; }

        public ViewModel(MainView owner)
        {
            _owner = owner;
            owner.CommandBindings.Add(new CommandBinding(AddNewAccountCommand, OnAddAccount));
            owner.CommandBindings.Add(new CommandBinding(AuthorizeNewAccountCommand, OnAuthorizeAccount));

            TimelineFeed            = new ObservableCollection<Tweet>();
            MentionsFeed            = new ObservableCollection<IMention>();
            MessageConversations    = new ObservableCollection<MessageConversation>();
            _updateTimer            = new DispatcherTimer(TimeSpan.FromSeconds(_updateRateSeconds), DispatcherPriority.Background, OnUpdateTimerTick, _owner.Dispatcher);
        }
       
        public string VerificationText
        {
            get { return _verificationText; }
            set 
            {
                _verificationText = value;
                //_isAddNewAccountVisible = _verificationText != null && _verificationText.Length >= 6;
                OnPropertyChanged();
            }
        }

        public bool IsAddNewAccountVisible
        {
            get { return _isAddNewAccountVisible; }
            set 
            {
                _isAddNewAccountVisible = value;
                OnPropertyChanged();
            }
        }

        public bool IsAuthorizeNewAccountVisible
        {
            get { return _isAuthorizeNewAccountVisible; }
            set 
            {
                _isAuthorizeNewAccountVisible = value;
                OnPropertyChanged();
            }
        }
        
        public void OnClosing()
        {
            if (_updateTimer != null)
                _updateTimer.Stop();
            StopStreaming();
            if (_accountAuth == null)
                return;

            // should always be up to date at this point
            //StorageManager.SaveAccount(_accountAuth);
            // persist the last x most recent
            StorageManager.SaveTweets(TimelineFeed.OrderByDescending(t => t.CreatedAt).Take(_numTweetsToPersist));
        }

        public void OnLoaded()
        {
            //_twitterService = new TwitterService(TwitterAccountAuth.ConsumerKey, TwitterAccountAuth.ConsumerSecret);
            // see if we got any saved accounts
            var attemptRestoredAcct = StorageManager.RestoreAccount();
            if (attemptRestoredAcct != null)
            {
                _accountAuth = attemptRestoredAcct;
                //_twitterService.AuthenticateWith(_accountAuth.Token, _accountAuth.TokenSecret);
                TwitterCredentials.SetCredentials(_accountAuth.Token, _accountAuth.TokenSecret, TwitterAccountAuth.ConsumerKey, TwitterAccountAuth.ConsumerSecret);
                UpdateNow(true);
            }
            else 
                IsAuthorizeNewAccountVisible = true;
        }

        internal void OnAddAccount(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(VerificationText) || VerificationText.Length < 6)
            {
                MessageBox.Show("Invalid verification# entered, please double check the site");
                return;
            }

            _isAuthInProgress = false;
            TwitterCredentials.SetCredentials(_accountAuth.Token, _accountAuth.TokenSecret, TwitterAccountAuth.ConsumerKey, TwitterAccountAuth.ConsumerSecret);
            var validatedCredentials = CredentialsCreator.GetCredentialsFromVerifierCode(VerificationText, _tmpAppCredentials);
            _accountAuth = new TwitterAccountAuth { Token = validatedCredentials.AccessToken, TokenSecret = validatedCredentials.AccessTokenSecret, Verification = VerificationText };
            StorageManager.SaveAccount(_accountAuth);
            IsAuthorizeNewAccountVisible = false;
            IsAddNewAccountVisible = false;
            UpdateNow(true);
        }

        internal void OnAuthorizeAccount(object sender, RoutedEventArgs e)
        {
            if (_isAuthInProgress)
                return;
            _isAuthInProgress = true;
            _tmpAppCredentials = CredentialsCreator.GenerateApplicationCredentials(TwitterAccountAuth.ConsumerKey, TwitterAccountAuth.ConsumerSecret);
            var uri = CredentialsCreator.GetAuthorizationURL(_tmpAppCredentials);
            Process.Start(uri);
        }

        private void StartStreaming()
        {
#if !OFFLINE
            if (_userStream != null)
                StopStreaming();

            _userStream = Stream.CreateUserStream();

            _userStream.TweetCreatedByAnyone        += OnTweetCreated;
            _userStream.TweetFavouritedByAnyone     += OnTweetFavorited;
            _userStream.TweetUnFavouritedByAnyone   += OnTweetUnfavorited;
            _userStream.MessageReceived             += OnMessageReceived;
            _userStream.MessageSent                 += OnMessageSent;

            // do this on threadpool to not block our thread
            _streamWorker = new BackgroundWorker();
            _streamWorker.DoWork += (o,e) => 
            { 
                _userStream.StartStreamAsync(); 
                Debug.WriteLine("BGW fallthrough");
            };
            _streamWorker.RunWorkerAsync();
#endif
        }

        private void OnMessageSent(object sender, Tweetinvi.Core.Events.EventArguments.MessageEventArgs e)
        {
        }

        private void OnMessageReceived(object sender, Tweetinvi.Core.Events.EventArguments.MessageEventArgs e)
        {
        }

        private void OnTweetUnfavorited(object sender, Tweetinvi.Core.Events.EventArguments.TweetFavouritedEventArgs e)
        {
        }

        private void OnTweetFavorited(object sender, Tweetinvi.Core.Events.EventArguments.TweetFavouritedEventArgs e)
        {
        }

        private void OnTweetCreated(object sender, Tweetinvi.Core.Events.EventArguments.TweetReceivedEventArgs e)
        {
            //tweet created by anyone
            _owner.Dispatcher.Invoke(() => TimelineFeed.Add(Tweet.FromApi(e.Tweet)));
        }

        private void StopStreaming()
        {
            if (_userStream == null)
                return;
            _userStream.StopStream();
            _userStream.TweetCreatedByAnyone        -= OnTweetCreated;
            _userStream.TweetFavouritedByAnyone     -= OnTweetFavorited;
            _userStream.TweetUnFavouritedByAnyone   -= OnTweetUnfavorited;
            _userStream.MessageReceived             -= OnMessageReceived;
            _userStream.MessageSent                 -= OnMessageSent;
            _streamWorker.Dispose();
        }

        private void UpdateNow(bool loadPersisted)
        {
            ThreadPool.QueueUserWorkItem(o =>
            {
                var tweetsToAdd = loadPersisted ? StorageManager.RestoreTweets().ToList() : new List<Tweet>();
                long? mostRecentId = null;
                // ?? operator cant lift nullable types RIP
                if (!loadPersisted && TimelineFeed.Count > 0) 
                    mostRecentId = TimelineFeed.First().Id;
                else if (loadPersisted && tweetsToAdd.Count > 0)
                    mostRecentId = tweetsToAdd.First().Id;

                if (loadPersisted)
                {
                    // immediately add the persisted stuff before hitting api
                    var copy = tweetsToAdd.ToList();
                    tweetsToAdd.Clear();
                    _owner.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var tw in copy)
                            TimelineFeed.Add(tw);
                    });
                }
    #if !OFFLINE
                tweetsToAdd.AddRange(Timeline.GetHomeTimeline(mostRecentId != null ? 
                    new HomeTimelineRequestParameters { SinceId = mostRecentId.Value, MaximumNumberOfTweetsToRetrieve = _maxTweetBackfill} : 
                    new HomeTimelineRequestParameters { MaximumNumberOfTweetsToRetrieve = _maxTweetBackfill }).Select(Tweet.FromApi));
    #endif
                // always trigger this in case we dont get any new tweets but want to update timestamps
                _owner.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var tw in tweetsToAdd.OrderByDescending(t => t.CreatedAt))
                        TimelineFeed.Add(tw);

                    foreach (var tweet in TimelineFeed)//.Where(t => !tweetsToAdd.Contains(t)))
                        tweet.InvalidateTimestamp();

                    if (loadPersisted)
                        StartStreaming();

                    //var testTweet = TimelineFeed.FirstOrDefault();
                    //if (testTweet != null)
                    //    testTweet.Children.Add(new Tweet { Text = "Child dummy", CreatedAt = DateTime.Now,ScreenName = "FART" });
                });
            });
        }

        private void OnUpdateTimerTick(object sender, EventArgs e)
        {
            // updates come in from streaming, however we will sort and update times on this timer still :/
            //UpdateNow(false);

            // want newest in index 0 this sucks :(

            //TimelineFeed = new ObservableCollection<Tweet>(TimelineFeed.OrderBy(t => t.CreatedAt));
            foreach (var tweet in TimelineFeed)
                tweet.InvalidateTimestamp();
        }
    }
}

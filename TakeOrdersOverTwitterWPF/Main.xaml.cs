using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dimebrain.TweetSharp;
using Dimebrain.TweetSharp.Model;
using Dimebrain.TweetSharp.Extensions;
using Dimebrain.TweetSharp.Fluent;
using System.Xml.Linq;
using TakeOrdersOverTwitterWPF.Properties;
using System.Collections.ObjectModel;
using System.Windows.Markup;
using System.Diagnostics;
using System.Xml;

namespace TakeOrdersOverTwitterWPF
{
    public partial class Main : Window
    {
        public Main()
        {
            InitializeComponent();

        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Properties.Settings.Default.Save();
        }

        DispatcherTimer twitterTimer = null;

        private static OAuthToken GetRequestToken(string consumerKey, string consumerSecret)
        {
            var requestToken = FluentTwitter.CreateRequest()
                .Authentication.GetRequestToken(consumerKey, consumerSecret);

            var response = requestToken.Request();
            var result = response.AsToken();

            if (result == null)
            {
                var error = response.AsError();
                if (error != null)
                {
                    throw new Exception(error.ErrorMessage);
                }
            }

            return result;
        }

        private static void GetResponse(string response)
        {
            var identity = response.AsUser();
            if (identity != null)
            {
                MessageBox.Show("{0} authenticated successfully.", identity.ScreenName);
            }
            else
            {
                var error = response.AsError();
                if (error != null)
                {
                    MessageBox.Show(error.ErrorMessage);
                }
            }
        }

        private static OAuthToken GetAccessToken(string consumerKey, string consumerSecret, string token, string pin)
        {
            var accessToken = FluentTwitter.CreateRequest()
                .Authentication.GetAccessToken(consumerKey, consumerSecret, token, pin);

            var response = accessToken.Request();
            var result = response.AsToken();

            if (result == null)
            {
                var error = response.AsError();
                if (error != null)
                {
                    throw new Exception(error.ErrorMessage);
                }
            }

            return result;
        }

        private static OAuthToken GetAccessToken(string token, string pin)
        {
            var accessToken = FluentTwitter.CreateRequest()
                .Authentication.GetAccessToken(token, pin);

            var response = accessToken.Request();
            var result = response.AsToken();

            if (result == null)
            {
                var error = response.AsError();
                if (error != null)
                {
                    throw new Exception(error.ErrorMessage);
                }
            }

            return result;
        }

        OAuthToken accessToken;
        string consumerKey = Settings.Default.ConsumerKey;
        string consumerSecret = Settings.Default.ConsumerSecret;

       private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // get an authenticated request token from twitter
            var requestToken = GetRequestToken(consumerKey, consumerSecret);

            if (String.IsNullOrEmpty(Settings.Default.Token) || String.IsNullOrEmpty(Settings.Default.Secret))
            {

                // automatically starts the default web browser, sending the 
                // user to the authorization URL.
                FluentTwitter.CreateRequest()
                    .Authentication
                    .AuthorizeDesktop(consumerKey,
                                      consumerSecret,
                                      requestToken.Token);

                // user authorization occurs out of band, so wait here
                string pin = Microsoft.VisualBasic.Interaction.InputBox("Enter the PIN that Twitter gave you here!", "Twitter Authorization", "",  300, 300);
                if (!String.IsNullOrEmpty(pin))
                {
                    // exchange the unauthenticated request token with an authenticated access token,
                    // and remember to persist this authentication pair for future use
                    accessToken = GetAccessToken(consumerKey, consumerSecret, requestToken.Token, pin.Trim());
                    Settings.Default.Token = accessToken.Token;
                    Settings.Default.Secret = accessToken.TokenSecret;


                    //// make an authenticated call to twitter with the token and secret
                    //var verify = fluenttwitter.createrequest()
                    //    .authenticatewith(consumerkey, consumersecret, accesstoken.token, accesstoken.tokensecret)
                    //    .account().verifycredentials().asjson();

                    //var response = verify.request();
                    //getresponse(response);
                }
            }

           CheckForOrders(null, null); 
           this.twitterTimer = new DispatcherTimer(new TimeSpan(0, 1, 0), DispatcherPriority.Normal, CheckForOrders, this.Dispatcher);

        }

        private void CheckForOrders(object sender, EventArgs e)
        {
            if (!String.IsNullOrEmpty(Settings.Default.Token) && !String.IsNullOrEmpty(Settings.Default.Secret))
            {
                long lastOrderNum = Settings.Default.LastOrder;
                
                //Twitter Bug, t'aint me.
                if (lastOrderNum == 0) lastOrderNum = 1;
                
                TwitterClientInfo info = new TwitterClientInfo() { ClientName = "TweetSandwich", ClientVersion = "1.0" };

                var replies = FluentTwitter.CreateRequest(info)
                        .AuthenticateWith(consumerKey, consumerSecret, Settings.Default.Token, Settings.Default.Secret)
                        .Statuses()
                        .Mentions()
                        .Since(lastOrderNum)
                        .AsJson();
                LogMessage(String.Format("Making request: {0}", replies.ToString()));

                IEnumerable<TwitterStatus> statuses = replies.Request().AsStatuses();
                if (statuses != null)
                {
                    var statusesFiltered = from d in statuses
                                           where d.Id > lastOrderNum
                                           where d.Text.IndexOf(orderString.Text, StringComparison.OrdinalIgnoreCase) != -1
                                           orderby d.CreatedDate descending
                                           select d;

                    if (statusesFiltered.Count() > 0)
                    {
                        Settings.Default.LastOrder = statusesFiltered.Max(i => i.Id);
                        Settings.Default.Save();
                        System.Media.SystemSounds.Exclamation.Play();
                        PrintOrders(statusesFiltered);
                    }

                }
                else
                {
                    LogMessage("No orders.");
                }

            }
        }

        public void LogMessage(string msg)
        {
            listBox1.Items.Insert(0,DateTime.Now.ToString() + ": " + msg);
        }

        private void PrintOrders(IEnumerable<TwitterStatus> statuses)
        {
            foreach(TwitterStatus t in statuses)
            {
                LogMessage(String.Format("Found order from {0} from {1}.", t.User.Name, t.User.Location));

                UpdateOrderCanvas(t);

                PrintOrder(t);

                //PrintDialog dlg = new PrintDialog();
                //dlg.PrintVisual(orderCanvas, "Tweeted Order");
            }
            //throw new NotImplementedException();
        }

        private void PrintOrder(TwitterStatus t)
        {
            var streamInfo = Application.GetResourceStream(new Uri("resources/SandwichOrder.xaml",UriKind.Relative));
            FlowDocument doc = XamlReader.Load(streamInfo.Stream) as FlowDocument;
            doc.DataContext = t;
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.SystemIdle, new DispatcherOperationCallback(delegate { return null; }), null);
            PrintDialog dlg = new PrintDialog();
            dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,"Tweeted Sandwich Order");
        }

        private void UpdateOrderCanvas(TwitterStatus t)
        {
            orderAvatar.Source = new BitmapImage(new Uri(t.User.ProfileImageUrl));
            orderDateTime.Content = t.CreatedDate;
            orderLocation.Content = t.User.Location;
            orderName.Content = t.User.Name;
            orderTwitterName.Content = t.User.ScreenName;
            orderTweet.Text = t.Text;

        }

    }

    //public static class StringExtensions
    //{
    //    public static DateTime ParseTwitterDateTime(this string date)
    //    {
    //        string dayOfWeek = date.Substring(0, 3).Trim();
    //        string month = date.Substring(4, 3).Trim();
    //        string dayInMonth = date.Substring(8, 2).Trim();
    //        string time = date.Substring(11, 9).Trim();
    //        string offset = date.Substring(20, 5).Trim();
    //        string year = date.Substring(25, 5).Trim();
    //        string dateTime = string.Format("{0}-{1}-{2} {3}", dayInMonth, month, year, time);
    //        DateTime ret = DateTime.Parse(dateTime);
    //        return ret;
    //    }
    //}

    namespace BindableText
    {
        /// <summary>
        /// A subclass of the Run element that exposes a DependencyProperty property
        /// to allow data binding.
        /// </summary>
        public class BindableRun : Run
        {
            public static readonly DependencyProperty BoundTextProperty = DependencyProperty.Register("BoundText", typeof(string), typeof(BindableRun), new PropertyMetadata(new PropertyChangedCallback(BindableRun.onBoundTextChanged)));

            private static void onBoundTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
                ((Run)d).Text = (string)e.NewValue;
            }

            public String BoundText
            {
                get { return (string)GetValue(BoundTextProperty); }
                set { SetValue(BoundTextProperty, value); }
            }
        }
    }
}

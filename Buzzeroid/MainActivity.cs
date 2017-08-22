﻿using System;
using System.Threading.Tasks;
using System.Diagnostics;

using Android.App;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android.Graphics;
using Android.Animation;
using Runnable = Java.Lang.Runnable;

using Android.Support.V4.View;
using Android.Support.V4.Animation;
using Android.Support.V7.App;
using Android.Support.Design.Widget;
using Android.Support.V7.Widget;
using Microsoft.Azure.Mobile;
using Microsoft.Azure.Mobile.Analytics;
using Microsoft.Azure.Mobile.Crashes;
using Microsoft.Azure.Mobile.Push;
namespace Buzzeroid
{
	[Activity (Label = "Buzzeroid", MainLauncher = true, Icon = "@mipmap/icon")]
	public class MainActivity : AppCompatActivity
	{
		BuzzerApi buzzerApi;
		Stopwatch openedTime = new Stopwatch ();

		CoordinatorLayout mainCoordinator;
		CheckableFab fab;
		RecyclerView recycler;

		BuzzHistoryAdapter adapter;

		FrameLayout notificationFrame;

		protected override void OnCreate (Bundle savedInstanceState)
		{
            // This should come before MobileCenter.Start() is called
            Push.PushNotificationReceived += (sender, e) => {

                // Add the notification message and title to the message
                var summary = $"Push notification received:" +
                                    $"\n\tNotification title: {e.Title}" +
                                    $"\n\tMessage: {e.Message}";

                // If there is custom data associated with the notification,
                // print the entries
                if (e.CustomData != null)
                {
                    summary += "\n\tCustom data:\n";
                    foreach (var key in e.CustomData.Keys)
                    {
                        summary += $"\t\t{key} : {e.CustomData[key]}\n";
                    }
                }

                // Send the notification summary to debug output
                System.Diagnostics.Debug.WriteLine(summary);
                //Toast.MakeText(summary, 2, ToastLength.Long).Show();
            };
            MobileCenter.SetLogUrl("https://in-staging-south-centralus.staging.avalanch.es");
            MobileCenter.Start("1c0ed3e0-4e3d-4bb4-a41a-3680ac373f96",
                   typeof(Analytics), typeof(Crashes),typeof(Push));
            base.OnCreate (savedInstanceState);

			SetContentView (Resource.Layout.Main);

			var toolbar = FindViewById<Android.Support.V7.Widget.Toolbar> (Resource.Id.toolbar);
			SetSupportActionBar (toolbar);

			mainCoordinator = FindViewById<CoordinatorLayout> (Resource.Id.coordinatorLayout);

			// SETUP NOTIFICATION FRAME

			notificationFrame = FindViewById<FrameLayout> (Resource.Id.notifFrame);
			notificationFrame.Visibility = ViewStates.Invisible;

			var title = notificationFrame.FindViewById<TextView> (Resource.Id.notifTitle);
			title.Typeface = Typeface.CreateFromAsset (Resources.Assets, "DancingScript.ttf");

			/* Assign notification behavior (aka swipe-to-dismiss)
			 */
			var lp = (CoordinatorLayout.LayoutParams)notificationFrame.LayoutParameters;
			var nb = new NotificationBehavior ();
			nb.Dismissed += (sender, e) => AddNewBuzzEntry (wasOpened: false);
			lp.Behavior = nb;
			notificationFrame.LayoutParameters = lp;

			// SETUP FLOATING ACTION BUTTON

			fab = FindViewById<CheckableFab> (Resource.Id.fabBuzz);
			fab.Click += OnFabBuzzClick;

			/* Craft curved motion into FAB
			 */
			lp = (CoordinatorLayout.LayoutParams)fab.LayoutParameters;
			lp.Behavior = new FabMoveBehavior ();
			fab.LayoutParameters = lp;

			/* Spice up the FAB icon story
			 */
			if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
				fab.SetImageResource (Resource.Drawable.ic_fancy_fab_icon);

			recycler = FindViewById<RecyclerView> (Resource.Id.recycler);
			recycler.HasFixedSize = true;
			adapter = new BuzzHistoryAdapter (this);
			recycler.SetLayoutManager (new LinearLayoutManager (this));
			recycler.SetItemAnimator (new DefaultItemAnimator ());
			recycler.SetAdapter (adapter);

			InitializeAdapter ();
		}

		public override bool OnCreateOptionsMenu (Android.Views.IMenu menu)
		{
			MenuInflater.Inflate (Resource.Menu.menu, menu);
			return true;
		}

		public override bool OnOptionsItemSelected (Android.Views.IMenuItem item)
		{
			if (item.ItemId == Resource.Id.action_notification) {
				notificationFrame.Visibility = ViewStates.Visible;
				float initialX = -(notificationFrame.Left + notificationFrame.Width + notificationFrame.PaddingLeft);
				notificationFrame.TranslationX = initialX;
				ViewCompat.Animate (notificationFrame)
						  .TranslationX (0)
						  .SetDuration (600)
						  .SetStartDelay (100)
						  .SetInterpolator (new Android.Support.V4.View.Animation.LinearOutSlowInInterpolator ())
						  .Start ();
                Analytics.TrackEvent("OnOptionsItemSelected");
                return true;
			}
			return base.OnOptionsItemSelected (item);
		}

		async void InitializeAdapter ()
		{
			try {
				await adapter.PopulateDatabaseWithStuff ();
				await adapter.FillUpFromDatabaseAsync ();
			} catch (Exception e) {
				Android.Util.Log.Error ("AdapterInitialize", e.ToString ());
			}
		}

		async Task<BuzzerApi> EnsureApi ()
		{
			if (buzzerApi != null)
				return buzzerApi;
			buzzerApi = await BuzzerApi.GetBuzzerApiAsync ();
			//ProcessFutureErrorStates (buzzerApi);
			return buzzerApi;
		}

		async void OnFabBuzzClick (object sender, System.EventArgs e)
		{
			if (fab.Checked)
				openedTime.Restart ();
			/* You can uncomment the following 2 lines if you want to
			 * test the IoT Hub part of the project, see companion
			 * BuzzerPi app in my GitHub repository.
			 */
			var api = await EnsureApi ();
			await api.SetBuzzerStateAsync (fab.Checked);
			if (!fab.Checked && openedTime.IsRunning) {
				openedTime.Stop ();
				AddNewBuzzEntry (wasOpened: true, duration: openedTime.Elapsed);
				if (notificationFrame.Visibility == ViewStates.Visible) {
					notificationFrame.Visibility = ViewStates.Invisible;
					notificationFrame.TranslationX = 1;
				}
			}
		}

		async void ProcessFutureErrorStates (BuzzerApi api)
		{
			while (true) {
				var result = await api.GetNextStateStatusAsync ();
				if (!result)
					Snackbar.Make (mainCoordinator, "Failed to send buzz", Snackbar.LengthLong)
							.Show ();
			}
		}

		async void AddNewBuzzEntry (bool wasOpened, TimeSpan? duration = null)
		{
			try {
				await adapter.AddNewEntryAsync (new HistoryEntry {
					DidOpen = wasOpened,
					EventDate = DateTime.UtcNow,
					DoorOpenedTime = duration ?? TimeSpan.Zero
				});
			} catch (Exception e) {
				Android.Util.Log.Error ("NewBuzzEntry", e.ToString ());
			}
		}
	}
}



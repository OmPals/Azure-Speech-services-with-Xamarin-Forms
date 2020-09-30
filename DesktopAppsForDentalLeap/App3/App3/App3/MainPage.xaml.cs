using Plugin.AudioRecorder;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using System.IO;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.ServiceBus;

namespace App3
{
	// Learn more about making custom code visible in the Xamarin.Forms previewer
	// by visiting https://aka.ms/xamarinforms-previewer
	public partial class MainPage : ContentPage
	{
		static string _storageConnection = "DefaultEndpointsProtocol=https;AccountName=audiorecordings;AccountKey=YxDLh1+Ur1HGbP0xpaycpm+Y5CX/4+MPZSjHUpwsT3Nc4wdgQrxSMkGkdTYc320/Kcf4X0OIiejlZnyljyyy2A==;EndpointSuffix=core.windows.net";
		static CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(_storageConnection);
		static CloudBlobClient cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();
		static CloudBlobContainer cloudBlobContainer = cloudBlobClient.GetContainerReference("audio");
		const string ServiceBusConnectionString = "Endpoint=sb://client-to-function-bus.servicebus.windows.net/;SharedAccessKeyName=user1;SharedAccessKey=chvCXMcWYQuiL4CKvOOq2L4r7wwM/7ihhcLTTdwMNFA=";
		const string QueueName = "queue1";
		static IQueueClient queueClient;

		AudioRecorderService recorder;
		AudioPlayer player;
		bool pause = false;
		Stopwatch stopwatch;
		int a = 1;
		

		public MainPage()
		{
			InitializeComponent();
			this.BindingContext = this;
			recorder = new AudioRecorderService()
			{
				StopRecordingOnSilence = false,
				StopRecordingAfterTimeout = false
			};
			stopwatch = new Stopwatch();
			player = new AudioPlayer();
			player.FinishedPlaying += Player_FinishedPlaying;
			StopWatchTime.Text = "00:00:00";
		}

		void TimerStart()
		{
			if (!stopwatch.IsRunning)
			{
				stopwatch.Start();

				Device.StartTimer(TimeSpan.FromMilliseconds(100), () =>
				{
					StopWatchTime.Text = stopwatch.Elapsed.ToString();

					if (!stopwatch.IsRunning)
					{
						return false;
					}
					else
					{
						return true;
					}
				}
				);
			}
		}


		async Task RecordAudio()
		{
			TimerStart();
			try
			{
				if (!recorder.IsRecording) //Record Btn clicked
				{
					PlayBtn.IsEnabled = false;

					//start recording audio
					var audioRecordTask = await recorder.StartRecording();

					RecordBtn.Text = "Stop Recording";
					RecordBtn.IsEnabled = true;

					await audioRecordTask;

					//RecordBtn.IsEnabled = true;
					stopwatch.Stop();
					SaveBtn.IsEnabled = true;
					DiscardBtn.IsEnabled = true;
					//RecordBtn.IsEnabled = false;
					RecordBtn.Text = "Record";
					PlayBtn.IsEnabled = true;
				}
				else //Stop Btn clicked
				{
					//stop the recording...
					await recorder.StopRecording();

					await DisplayAlert("Done", "Recording has been done.", "OK");
				}
			}
			catch (Exception ex)
			{
				//blow up the app!
				throw ex;
			}
		}


		void PlayAudio()
		{
			TimerStart();
			try
			{
				var filePath = recorder.GetAudioFilePath();

				if (filePath != null)
				{
					PlayBtn.IsEnabled = false;
					//RecordBtn.IsEnabled = true;
					player.Play(filePath);
					PauseAudioBtn.IsEnabled = true;
				}
			}
			catch (Exception ex)
			{
				//blow up the app!
				throw ex;
			}
		}

		void Player_FinishedPlaying(object sender, EventArgs e)
		{
			PlayBtn.IsEnabled = true;
			PauseAudioBtn.IsEnabled = false;
			RecordBtn.IsEnabled = true;
			stopwatch.Reset();
		}

		private async void RecordBtn_Clicked(object sender, EventArgs e)
		{
			player.Pause();
			await RecordAudio();
		}

		private void PlayBtn_Clicked(object sender, EventArgs e)
		{
			stopwatch.Reset();
			PlayAudio();
			PauseAudioBtn.IsEnabled = true;
			pause = false;
		}

		private void PauseAudioBtn_Clicked(object sender, EventArgs e)
		{
			PlayBtn.IsEnabled = false;
			if (!pause)
			{
				pause = true;
				player.Pause();
				PauseAudioBtn.Text = "Play";
				stopwatch.Stop();
			}
			else
			{
				pause = false;
				player.Play();
				PauseAudioBtn.Text = "Pause";
				TimerStart();
			}
		}

		private void SaveBtn_Clicked(object sender, EventArgs e)
		{
			UploadAudio();
		}

		private void DiscardBtn_Clicked(object sender, EventArgs e)
		{
			stopwatch.Reset();
			DisplayAlert("Success", "Discarded!", "OK");
			RecordBtn.IsEnabled = true;
			PlayBtn.IsEnabled = false;
			PauseAudioBtn.IsEnabled = false;
			SaveBtn.IsEnabled = false;
			DiscardBtn.IsEnabled = false;
		}

		private async void UploadAudio()
		{
			RecordBtn.IsEnabled = false;

			await DisplayAlert("Success", "Audio will be uploaded after you tap OK.", "ok");
            try
            {
				await cloudBlobContainer.CreateIfNotExistsAsync();
				await cloudBlobContainer.SetPermissionsAsync(new BlobContainerPermissions
				{
					PublicAccess = BlobContainerPublicAccessType.Blob
				});
				string blobFileName = $"audio{a}.wav";
				a = a + 1;
				var blockBlob = cloudBlobContainer.GetBlockBlobReference(blobFileName);
				try
				{
					await UploadAudio(blockBlob, recorder.FilePath);
				}
				catch (Exception ex)
				{
					await DisplayAlert("ERROR", ex.Message, "OK");
				}

				await DisplayAlert("Audio uploaded", "Your Audio has been uploaded. Tap START to start transcripting the audio", "Start");

				queueClient = new QueueClient(ServiceBusConnectionString, QueueName);
				
				
				await SendMessagesAsync(blobFileName);
				await queueClient.CloseAsync();
				await DisplayAlert("Service Bus", "Your audio is going to get transcripted." + blobFileName, "OK");
			}
			catch (Exception ex)
            {
				await DisplayAlert("Something Wrong with Blob", ex.Message, "Ok");
            }
			/*try
			{
				await DependencyService.Get<IAudio>().FileSaveAsync(blockBlob, a);
				await DisplayAlert("Success", "Check for file named DownloadedFile" + a.ToString() + " in the Downloads folder of your phone.", "OK");
				a++;
			}
			catch (Exception ex)
			{
				await DisplayAlert("ERROR", ex.Message, "OK");
			}*/

			RecordBtn.IsEnabled = true;
			SaveBtn.IsEnabled = false;
			DiscardBtn.IsEnabled = false;
			stopwatch.Reset();
		}
		private static async Task UploadAudio(CloudBlockBlob blob, string filePath)
		{
			try
			{
				using (var fileStream = File.OpenRead(filePath))
				{
					await blob.UploadFromStreamAsync(fileStream);
				}
			}
			catch (Exception ex)
			{
				//
			}
		}

		static async Task SendMessagesAsync(string fileName)
		{
			try
			{
				// Create a new message to send to the queue
				string messageBody = $"https://audiorecordings.blob.core.windows.net/audio/{fileName}";
				var message = new Message(Encoding.UTF8.GetBytes(messageBody));

				// Write the body of the message to the console
				Console.WriteLine($"Sending message: {messageBody}");

				// Send the message to the queue
				await queueClient.SendAsync(message);
			}
			catch (Exception exception)
			{
				Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
			}
		}
	}
}

//tabs=4
//-----------------------------------------------------------------------------
// TITLE:		MainForm.cs
//
// FACILITY:	RSS to Morse tool
//
// ABSTRACT:	Reads news from Yahoo! RSS feeds and generates Morse code tones
//				or telegraph sounds. This is the main form for the app.
//
// ENVIRONMENT:	Microsoft.NET 2.0/3.5
//				Developed under Visual Studio.NET 2008
//				Also may be built under MonoDevelop 2.2.1/Mono 2.4+
//
// NOTE ON CODE AND CHAR SPEED SPINBOXES:  If the CharSpeed spinbox is auto-bound
//				to the Application Setting, the interaction between it and the
//				CodeSPeed spinbox is strange. When the CodeSpeed ValueChanged
//				event fires, and needs to increase the code speed to match
//				char speed cannot be less then code speed!), a complex series
//				of events takes plaace, including re-binding all of the settings
//				which resets the CodeSpeed bopx back to its pre-spin value. 
//				Making a long story short, manually binding the CharSpeed spinbox
//				avoids this problem. Weird.
//
// AUTHOR:		Bob Denny, <rdenny@dc3.com>
//
// Edit Log:
//
// When			Who		What
//----------	---		-------------------------------------------------------
// ??-Apr-10	rbd		Initial editing and development
// 28-Apr-10	rbd		1.1.0 Merge SoundPlayer support and make it the default.
//						For Mono, do not include null as first param to 
//						MessageBox.Show(). Mono has no sound so doesn't work!
//						Add Timing Comp control for tone start latency. Fix
//						tab ordering. Fix range of code speed control.
// 30-Apr-10	rbd		1.2.0 - Reorganize, add spark gap, resurrect DirectX,
//						move DX and TimingComp to separate form.  Add message
//						to cache only is really sent completely. New class
//						MorseMessage. Increase URL list capacity to 16. Handle
//						authenticated feeds!
// 01-May-10	rbd		1.3.0 - Make this runnable on systems which don't have
//						Microsoft.DirectX via second project which omits DX
//						sound classes and ref to DirectX.DirectSound
// 03-May-10	rbd		1.3.0 - Much better solution: Include the Managed DirectX
//						assemblies in the setup. This allows Fusion to find them
//						if the DirectX End User Runtime is not installed. Refactor
//						audio interfaces, now just two (tone and wav). Fix DX/Sound
//						switching (on close SoundCfg dialog).
// 03-May-10	rbd		1.3.2 - Switched to new PreciseDelay.Wait for timing, uses
//						multimedia timer. Make sounder test dits at 20WPM.
// 04-May-10	rbd		1.3.3 - Low level serial control for physical sounder, 
//						change test dits to 20WPM. Misc cleanups.
// 11-May-10	rbd		1.5.1 - Volume control!
// 13-May-10	rbd		1.5.1 - Feedburner, CNN, others don't send Content-Length
//						so no longer require it. Also, Feedburner sends EDT (daylight)
//						pubDates during DST, handle that.
// 17-May-10	rbd		1.5.1 - Fox sends EST on EDT times, detect and subtract an
//						hour. Add Unknown Char Handler so prevent error code being
//						send for unknown chars (usually unicode stuff) that isn't 
//						caught by the fixup code.
// 21-May-10	rbd		1.5.0 - Change doc root	from index.html to keyer.html so 
//						can share same doc folder with keyer in end-user install.
//
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Windows.Forms;
using System.Xml;

using com.dc3.morse;

namespace com.dc3
{
	public partial class MainForm : Form
	{
		public enum CodeMode { International, American }
		public enum SoundMode { Tone, Spark, Sounder }
		//
		// Represents an RSS item with a correct pubDate
		//
		private class datedRssItem
		{
			public DateTime pubDate { get; set; }
			public XmlNode rssItem { get; set; }
		}

		private class MorseMessage
		{
			public string Title { get; set; }
			public string Contents { get; set; }
		}
		
		//
		// Settings
		//
		private CodeMode _codeMode;
		private SoundMode _soundMode;
		private int _pollInterval;
		private int _codeSpeed;
		private int _charSpeed;
		private bool _directX;
		private int _toneFreq;
		private int _timingComp;
		private int _sounderNum;
		private int _sparkNum;
		private int _storyAge;
		private string _feedUrl;
		private int _serialPortNum;
		private bool _useSerial;

		//
		// State variables
		//
		private Thread _runThread = null;
		private bool _run;
		private Dictionary<string, DateTime> _titleCache = new Dictionary<string, DateTime>();
		private Thread _titleExpireThread = null;
		private int _msgNr = 1;
		private ITone _tones;
		private IAudioWav _sounder;
		private IAudioWav _spark;
		private DateTime _lastPollTime;
		private ComPortCtrl _serialPort;

		//
		// Form ctor and event methods
		//
		public MainForm()
		{
			InitializeComponent();
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			Debug.Print("Load");
			_pollInterval = (int)nudPollInterval.Value;
			_codeSpeed = (int)nudCodeSpeed.Value;
			// -- MANUALLY BOUND - SEE NOTE ABOVE --
			nudCharSpeed.Value = Properties.Settings.Default.CharSpeed;
			nudCharSpeed.Minimum = (decimal)_codeSpeed;
			_charSpeed = (int)nudCharSpeed.Value;
			// -------------------------------------
			_directX = Properties.Settings.Default.DirectX;
			_timingComp = (int)Properties.Settings.Default.TimingComp;
			_toneFreq = (int)nudToneFreq.Value;
			_sounderNum = (int)nudSounder.Value;
			_sparkNum = (int)nudSpark.Value;
			_storyAge = (int)nudStoryAge.Value;
			_feedUrl = cbFeedUrl.Text;
			_serialPortNum = (int)nudSerialPort.Value;
			_useSerial = chkUseSerial.Checked;
			_serialPort = null;
			_run = false;

			if (Properties.Settings.Default.CodeMode == 0)
				rbInternational.Checked = true;									// Triggers CheckedChanged (typ.)
			else
				rbAmerican.Checked = true;
			switch (Properties.Settings.Default.SoundMode)
			{
				case 0:
					rbTone.Checked = true;
					break;
				case 1:
					rbSounder.Checked = true;
					break;
				case 2:
					rbSpark.Checked = true;
					break;
			}

			foreach (string uri in Properties.Settings.Default.LRU)
				cbFeedUrl.Items.Add(uri);
			if (cbFeedUrl.Text == "")											// Force something into feed URL
				cbFeedUrl.Text = Properties.Settings.Default.LRU[0];

			_titleExpireThread = new Thread(new ThreadStart(TitleExpireThread));
			_titleExpireThread.Name = "Title Expiry";
			_titleExpireThread.Start();

			SetupSound();
			PreciseDelay.Initialize();

			statBarLabel.Text = "Ready";
			statBarCrawl.Text = "";
			UpdateUI();
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (_titleExpireThread != null)
			{
				_titleExpireThread.Interrupt();
				_titleExpireThread.Join(1000);
			}
			if (_runThread != null)
			{
				_runThread.Interrupt();
				_runThread.Join(1000);
			}
			if (_serialPort != null)
				_serialPort.Close();
			_serialPort = null;
			PreciseDelay.Cleanup();
			Properties.Settings.Default.LRU.Clear();
			foreach (string uri in cbFeedUrl.Items)
				Properties.Settings.Default.LRU.Add(uri);
			Properties.Settings.Default.Save();
		}

		private void cbFeedUrl_TextChanged(object sender, EventArgs e)
		{
			_feedUrl = cbFeedUrl.Text;
			UpdateUI();															// For disabling Start if empty
		}

		private void nudPollInterval_ValueChanged(object sender, EventArgs e)
		{
			_pollInterval = (int)nudPollInterval.Value;
		}

		private void nudStoryAge_ValueChanged(object sender, EventArgs e)
		{
			_storyAge = (int)nudStoryAge.Value;
		}

		private void nudCodeSpeed_ValueChanged(object sender, EventArgs e)
		{
			_codeSpeed = (int)nudCodeSpeed.Value;
			if (_codeSpeed > _charSpeed)
				nudCharSpeed.Value = (decimal)_codeSpeed;
			nudCharSpeed.Minimum = (decimal)_codeSpeed;

		}

		private void nudCharSpeed_ValueChanged(object sender, EventArgs e)
		{
			// -- MANUALLY BOUND - SEE NOTE ABOVE --
			_charSpeed = (int)nudCharSpeed.Value;
			Properties.Settings.Default.CharSpeed = (decimal)_charSpeed;
			// -------------------------------------
		}

		private void nudToneFreq_ValueChanged(object sender, EventArgs e)
		{
			_toneFreq = (int)nudToneFreq.Value;
			_tones.Frequency = _toneFreq;
			_tones.PlayFor(100);
		}

		private void nudSpark_ValueChanged(object sender, EventArgs e)
		{
			_sparkNum = (int)nudSpark.Value;
			_spark.SoundIndex = _sparkNum;
			_spark.PlayFor(100);
		}

		private void nudSounder_ValueChanged(object sender, EventArgs e)
		{
			_sounderNum = (int)nudSounder.Value;
			_sounder.SoundIndex = _sounderNum;
			_sounder.PlayFor(100);
		}

		private void nudSerialPort_ValueChanged(object sender, EventArgs e)
		{
			_serialPortNum = (int)nudSerialPort.Value;
		}

		private void rbInternational_CheckedChanged(object sender, EventArgs e)
		{
			if (rbInternational.Checked)
			{
				_codeMode = CodeMode.International;
				Properties.Settings.Default.CodeMode = 0;
			}
			UpdateUI();
		}

		private void rbAmerican_CheckedChanged(object sender, EventArgs e)
		{
			if (rbAmerican.Checked)
			{
				_codeMode = CodeMode.American;
				Properties.Settings.Default.CodeMode = 1;
			}
			UpdateUI();
		}

		private void rbTone_CheckedChanged(object sender, EventArgs e)
		{
			if (rbTone.Checked)
			{
				_soundMode = SoundMode.Tone;
				Properties.Settings.Default.SoundMode = 0;
			}
			UpdateUI();
		}

		private void rbSpark_CheckedChanged(object sender, EventArgs e)
		{
			if (rbSpark.Checked)
			{
				_soundMode = SoundMode.Spark;
				Properties.Settings.Default.SoundMode = 2;
			}
			UpdateUI();
		}

		private void rbSounder_CheckedChanged(object sender, EventArgs e)
		{
			if (rbSounder.Checked)
			{
				_soundMode = SoundMode.Sounder;
				Properties.Settings.Default.SoundMode = 1;
			}
			UpdateUI();
		}

		private void tbVolume_Scroll(object sender, EventArgs e)
		{
			_tones.Volume = _sounder.Volume = _spark.Volume = tbVolume.Value / 10.0F;
			if (_run) return;
			switch (_soundMode)
			{
				case SoundMode.Tone:
					_tones.PlayFor(60);
					break;
				case SoundMode.Spark:
					_spark.PlayFor(60);
					break;
				case SoundMode.Sounder:
					_sounder.PlayFor(60);
					break;
			}
		}

		private void chkUseSerial_CheckedChanged(object sender, EventArgs e)
		{
			_useSerial = chkUseSerial.Checked;
			UpdateUI();
		}

		private void picTestSerial_Click(object sender, EventArgs e)
		{
			try
			{
				picTestSerial.Enabled = false;
				ComPortCtrl S = new ComPortCtrl();
#if MONO_BUILD
				S.Open("/dev/tty.serial" + _serialPortNum.ToString());
#else
				S.Open("COM" + _serialPortNum.ToString());
#endif
				for (int i = 0; i < 4; i++)										// 4 dits @ 20 WPM
				{
					S.RtsEnable = true;
					PreciseDelay.Wait(60);
					S.RtsEnable = false;
					PreciseDelay.Wait(60);
				}
				S.Close();
				S.Dispose();
				MessageBox.Show("Test complete, 4 dits sent.", "Sounder Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Sounder Test", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
			picTestSerial.Enabled = true;
		}


		private void btnClearCache_Click(object sender, EventArgs e)
		{
			TitleExpire();
			MessageBox.Show("Seen stories have been forgotten", "RSS to Morse", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		private void mnuUrlResetToDefault_Click(object sender, EventArgs e)
		{
			//
			// This is really obscure. I found an article on the almighty StackOverflow
			// http://stackoverflow.com/questions/49269/reading-default-application-settings-in-c
			// which showed how to get the original/default value of an application
			// setting. But doing this does not return a collection - it returns raw XML.
			// So we have to parse that and get the LRU list that way. Oh well...
			//
			string lru = (string)Properties.Settings.Default.Properties["LRU"].DefaultValue;
			XmlDocument listXml = new XmlDocument();
			listXml.LoadXml(lru);
			XmlNodeList items = listXml.SelectNodes("/ArrayOfString/string");
			cbFeedUrl.Items.Clear();
			foreach (XmlNode item in items)
				cbFeedUrl.Items.Add(item.InnerText);
			cbFeedUrl.Text = items.Item(0).InnerText;
		}

		private void llSoundCfg_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			picSoundCfg_Click(sender, new EventArgs());
		}

		private void picSoundCfg_Click(object sender, EventArgs e)
		{
			SoundCfgForm sf = new SoundCfgForm();
			sf.TimingComp = (int)Properties.Settings.Default.TimingComp;
			sf.UseDirectX = Properties.Settings.Default.DirectX;
			if (sf.ShowDialog(this) == DialogResult.OK)
			{
				_timingComp = sf.TimingComp;
				_tones.StartLatency = _timingComp;
				_sounder.StartLatency = _timingComp;
				_spark.StartLatency = _timingComp;
				Properties.Settings.Default.TimingComp = (decimal)_timingComp;
				if (_directX != sf.UseDirectX)									// Switching sound technology
				{
					_directX = sf.UseDirectX;
					SetupSound();
				}
				else
					_directX = sf.UseDirectX;
				Properties.Settings.Default.DirectX = _directX;
			}
		}

		private void llHelp_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			picHelp_Click(sender, new EventArgs());
		}

		private void picHelp_Click(object sender, EventArgs e)
		{
			System.Diagnostics.Process.Start(Path.GetDirectoryName(Application.ExecutablePath) + "\\doc\\rssmorse.html");
		}

		private void llRSSFeeds_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			picRSS_Click(sender, new EventArgs());
		}

		private void picRSS_Click(object sender, EventArgs e)
		{
			System.Diagnostics.Process.Start("http://news.yahoo.com/page/rss");
		}

		private void SetupSound()
		{
#if !MONO_BUILD
			if (_directX)
			{
				_tones = new DxTones(this, 1000);
				_sounder = new DxSounder(this);
				_spark = new DxSpark(this);
			}
			else
			{
				_tones = new SpTones();
				_sounder = new SpSounder();
				_spark = new SpSpark();
			}
#else
			_tones = new SpTones();
			_sounder = new SpSounder();
			_spark = new SpSpark();
#endif
			_tones.Frequency = _toneFreq;
			_tones.Volume = tbVolume.Value / 10.0F;
			_tones.StartLatency = _timingComp;
			_sounder.SoundIndex = _sounderNum;
			_sounder.Volume = tbVolume.Value / 10.0F;
			_sounder.StartLatency = _timingComp;
			_spark.SoundIndex = _sparkNum;
			_spark.Volume = tbVolume.Value / 10.0F;
			_spark.StartLatency = _timingComp;
		}

		private void UpdateUI()
		{
			bool enable = !_run;
			btnStartStop.Enabled = (cbFeedUrl.Text != "");
			cbFeedUrl.Enabled = enable;
			nudCodeSpeed.Enabled = enable;
			nudCharSpeed.Enabled = enable;
			nudPollInterval.Enabled = enable;
			nudStoryAge.Enabled = enable;
			nudSerialPort.Enabled = enable;
			chkUseSerial.Enabled = enable;
			rbAmerican.Enabled = enable;
			rbInternational.Enabled = enable;
			picTestSerial.Enabled = enable & !chkUseSerial.Checked;

			enable = enable & !_useSerial;
			nudSpark.Enabled = enable && rbSpark.Checked;
			nudSounder.Enabled = enable & rbSounder.Checked;
			nudToneFreq.Enabled = enable & rbTone.Checked;
			rbTone.Enabled = enable;
			rbSounder.Enabled = enable;
			rbSpark.Enabled = enable;
			llSoundCfg.Enabled = enable;
			picSoundCfg.Enabled = enable;
		}

		private void btnStartStop_Click(object sender, EventArgs e)
		{
			if (!_run)
			{
				if (!cbFeedUrl.Items.Contains(cbFeedUrl.Text))					// Add new URIs to combo box when actually USED!
				{
					while (cbFeedUrl.Items.Count > 16)							// Safety catch only
						cbFeedUrl.Items.RemoveAt(0);
					if (cbFeedUrl.Items.Count == 16)								// If full, remove last item
						cbFeedUrl.Items.RemoveAt(15);
					cbFeedUrl.Items.Insert(0, cbFeedUrl.Text);					// Insert new item at top
				}
				else
				{
					int idx = cbFeedUrl.FindStringExact(cbFeedUrl.Text);
					if (idx > 0)												// If not already at top, move to top
					{
						string uri = cbFeedUrl.Text;							// Save this, next stmt clears text!
						cbFeedUrl.Items.RemoveAt(idx);
						cbFeedUrl.Items.Insert(0, uri);
						cbFeedUrl.Text = uri;
					}
				}

				_runThread = new Thread(new ThreadStart(Run));
				_runThread.Name = "RSS engine";
				_runThread.Start();
				btnStartStop.Text = "Stop";
				_run = true;
			}
			else
			{
				switch (_soundMode)
				{
					case SoundMode.Tone:
						_tones.Stop();
						break;
					case SoundMode.Spark:
						_spark.Stop();
						break;
					case SoundMode.Sounder:
						_sounder.Stop();
						break;
				}
				if (_runThread != null)
				{
					_runThread.Interrupt();
					_runThread.Join(1000);
				}
				if (_serialPort != null)
					_serialPort.Close();
				_serialPort = null;
				btnStartStop.Text = "Start";
				_run = false;
			}
			statBarCrawl.Text = "";
			statBarLabel.Text = "Ready";
			UpdateUI();
		}

		//
		// Cross-thread methods for the worker thread and the status bar
		//
		delegate void SetTextCallback(string text);

		private void SetStatus(string text)
		{
			if (this.statusStrip1.InvokeRequired)
			{
				SetTextCallback d = new SetTextCallback(SetStatus);
				this.Invoke(d, new object[] { text });
			}
			else
			{
				statBarLabel.Text = text;
			}
		}

		private void SetCrawler(string text)
		{
			if (this.statusStrip1.InvokeRequired)
			{
				SetTextCallback d = new SetTextCallback(SetCrawler);
				this.Invoke(d, new object[] { text });
			}
			else
			{
				statBarCrawl.Text = text;
			}
		}

		private void AddToCrawler(string text)
		{
			if (this.statusStrip1.InvokeRequired)
			{
				SetTextCallback d = new SetTextCallback(AddToCrawler);
				this.Invoke(d, new object[] { text });
			}
			else
			{
				statBarCrawl.Text += text;
			}
		}

		//
		// Other Logic
		//

		//
		// Strip stuff from text that cannot be sent by Morse Code
		// HTML-decodes, then removes HTML tags and non-Morse characters,
		// and finally removes runs of more than one whitespace character,
		// replacing with a single space and uppercases it.
		//
		private string GetMorseInputText(string stuff)
		{
			string buf = HttpUtility.HtmlDecode(stuff);							// Decode HTML entities, etc.
			buf = Regex.Replace(buf, "<[^>]*>", " ");							// Remove HTML tags completely
			buf = Regex.Replace(buf, "[\\~\\^\\%\\|\\#\\<\\>\\*\\u00A0]", " ");	// Some characters we don't have translations for => space
			buf = Regex.Replace(buf, "[\\‘\\’\\`]", "'");						// Unicode left/right single quote, backtick -> ASCII single quote
			buf = Regex.Replace(buf, "[\\{\\[]", "(");							// Left brace/bracket -> left paren
			buf = Regex.Replace(buf, "[\\}\\]]", ")");							// Right brace/bracket -> Right paren
			buf = Regex.Replace(buf, "[—–]", "-");								// Unicode emdash/endash -> hyphen
			buf = Regex.Replace(buf, "\\s\\s+", " ").Trim().ToUpper();			// Compress running whitespace, fold all to upper case

			return buf;
		}

		//
		// Tries to get a correct pubDate (local time) from the RSS <item> node.
		//
		// This would be much easier if the @#$%^& date in pubDate wasn't the
		// old lazy-man's unix strftime()/RFC822 format, with its "whatever"
		// time zone abbrevations!!!! FeedBurner uses "EST" egad.
		//
		private DateTime GetPubDateUtc(XmlNode item)
		{
			DateTime corrDate;

			XmlNode n = item.SelectSingleNode("pubDate");						// Some feeds don't have this, so use now()
			if (n == null)
				return DateTime.Now;

			string dateStr = n.InnerText;
			if (!DateTime.TryParse(dateStr, out corrDate))
			{
				//
				// Probably an RFC 822 time with text time zone other than 'GMT"
				// Try FeedBurner's EST/EDT. FOx sends EST with EDT time so can
				// get times in future :-( Try subtracting an hour as a guess.
				//
				string buf;
				if (dateStr.Contains("EST"))									// FeedBurner sends EST/EDT times :-(
					buf = dateStr.Replace(" EST", "-0500");
				else if (dateStr.Contains("EDT"))
					buf = dateStr.Replace(" EDT", "-0400");
				else
					return DateTime.MinValue;									// [sentinel] No luck
				if (DateTime.TryParse(buf, out corrDate))
				{
					if (corrDate > DateTime.Now)								// If in future (e.g. Fox/Science)
					{
						corrDate = corrDate.AddHours(-1);
						if (corrDate > DateTime.Now)
							return DateTime.MinValue;
						else
							return corrDate;
					}
					else
						return corrDate;
				}
				else
				{
					return DateTime.MinValue;									// [sentinel] No luck
				}
			}
			else																// Converted successfully!
				return corrDate;
		}

		//
		// Get the XML from a feed supporting a URL of the form
		//   http://user:pass@domain/...
		// so can get authenticated feeds (e.g. Twitter). Cannot
		// do this with XmlDocument.Load();
		//
		private string GetAuthFeedXml(string authUrl)
		{
			string xml, buf;
			HttpWebResponse rsp = null;											// [sentinel]
			NetworkCredential cred = null;										// [sentinel]

			buf = authUrl;
			buf = Regex.Replace(buf, "http://([^\\:]*\\:[^\\@]*)\\@.*", "$1");
			if (buf != authUrl)													// If found basic auth
			{
				string[] bits = buf.Split(new char[] { ':' });
				if (bits.Length != 2)
					throw new ApplicationException("Basic auth format is incorrect, see help");
				cred = new NetworkCredential(bits[0], bits[1]);
			}

			try
			{
				HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(authUrl);
				if (cred != null)
					req.Credentials = cred;
				rsp = (HttpWebResponse)req.GetResponse();
				if (rsp.StatusCode != HttpStatusCode.OK)
					throw new ApplicationException("RSS server returned " + rsp.StatusDescription + ", check the URL");
				// Feedburner and some other feeds have 0 or -1 Content-Length. Sad but true.
				//if (rsp.ContentLength <= 0)
				//    throw new ApplicationException("RSS server can't return feed data, check the URL");
				using (Stream rspStrm = rsp.GetResponseStream())
				{
					using (StreamReader rdr = new StreamReader(rspStrm))
						xml = rdr.ReadToEnd();
				}
				if (xml.Length == 0)
					throw new ApplicationException("RSS server can't return feed data, check the URL");
				return xml;
			}
			finally
			{
				if (rsp != null) rsp.Close();
			}
		}
		//
		// Remove titles older than 'titleAge' from the cache
		// Runs as separate thread.
		//
		private void TitleExpireThread()
		{
			try
			{
				while (true)
				{
					TitleExpire();
					Thread.Sleep(300000);										// Every 5 min
				}
			}
			catch (ThreadInterruptedException)
			{
				return;
			}
		}

		//
		// Worker logic, also used by manual expire button
		//
		private void TitleExpire()
		{
			DateTime expiryTime = DateTime.Now.AddMinutes(-_storyAge);
			lock (_titleCache)
			{
				List<string> oldTitles = new List<string>();
				foreach (string title in _titleCache.Keys)
				{
					if (_titleCache[title] < expiryTime)
						oldTitles.Add(title);
				}
				foreach (string title in oldTitles)
					_titleCache.Remove(title);
			}
		}
		//
		// Sender delegate for the CwCom mode of Morse. This gets timing arrays, and calls
		// the tone generator or twiddles the RTS line on the serial port
		// (it's not really CwCom :-)).
		//
		private void Send(Int32[] code, string text)
		{
			for (int i = 0; i < code.Length; i++)
			{
				if (code[i] > 0)
				{
					if (_useSerial)
					{
						_serialPort.RtsEnable = true;
						PreciseDelay.Wait(code[i]);
						_serialPort.RtsEnable = false;
					}
					else
					{
						switch (_soundMode)
						{
							case SoundMode.Tone:
								_tones.PlayFor(code[i]);
								break;
							case SoundMode.Spark:
								_spark.PlayFor(code[i]);
								break;
							case SoundMode.Sounder:
								_sounder.PlayFor(code[i]);
								break;
						}
					}
				}
				else
					PreciseDelay.Wait(_useSerial ? -code[i] : -code[i] - _timingComp);
			}
			string ct = Regex.Replace(text, "\\s+", " ");						// Remove running spaces from crawler
			AddToCrawler(ct);
		}
		//
		// Unknown character handler delegate. When this is active, a
		// space will be sent instead of the error code. Here we just
		// put the character into the crawler in [] so it will show.
		// for debugging, send extra stuff that will help us ID the
		// character for possible inclusion into the cleanup code.
		//
		private void HandleUnkChar(char ch)
		{
#if DEBUG
			string msg = " ['" + ch + "'";
			msg += " U+" + ((int)ch).ToString("X4") + "]";
#else
			string msg = " [" + ch + "] ";
#endif
			AddToCrawler(msg);
		}

		private void Run()
		{
			try
			{
				Morse M = new Morse();
				M.Mode = (_codeMode == CodeMode.International ? Morse.CodeMode.International : Morse.CodeMode.American);
				M.CharacterWpm = _charSpeed;
				M.WordWpm = _codeSpeed;
				M.UnknownCharacter = HandleUnkChar;

				_msgNr = 1;														// Start with message #1

				if (_useSerial)
				{
					_serialPort = new ComPortCtrl();
#if MONO_BUILD
					_serialPort.Open("/dev/tty.serial" + _serialPortNum.ToString());
#else
					_serialPort.Open("COM" + _serialPortNum.ToString());
#endif
				}

				// Remember the state of the title cache, we have a clear button!
				//lock (_titleCache) { _titleCache.Clear(); }					// Clear title cache on start

				while (true)
				{
					_lastPollTime = DateTime.Now;

					SetStatus("Getting RSS feed data...");
					XmlDocument feedXml = new XmlDocument();
					//feedXml.Load(_feedUrl);
					feedXml.LoadXml(GetAuthFeedXml(_feedUrl));

					XmlNodeList items = feedXml.SelectNodes("/rss/channel/item");
					if (items.Count == 0)
						throw new ApplicationException("This does not look like an RSS feed");

					List<datedRssItem> stories = new List<datedRssItem>();
					foreach (XmlNode item in items)
					{
						DateTime pubUtc = GetPubDateUtc(item);					// See comments above, sick hack
						if (pubUtc == DateTime.MinValue)
							continue;											// Bad pubDate, skip this story
						//
						// OK we have a story we can use, as we were able to parse the date.
						//
						datedRssItem ni = new datedRssItem();
						ni.pubDate = pubUtc;
						ni.rssItem = item;
						stories.Add(ni);
					}

					if (stories.Count == 0)
						throw new ApplicationException("This RSS feed has strange or missing pub dates, thus it can't be used, sorry.");

					//
					// Create a list of strings which are the final messages to be send in Morse
					//
					List<MorseMessage> messages = new List<MorseMessage>();
					foreach (datedRssItem story in stories)
					{
						string title = GetMorseInputText(story.rssItem.SelectSingleNode("title").InnerText);
						lock (_titleCache)
						{
							if (_titleCache.ContainsKey(title))
								continue;										// Recently sent, skip
						}

						// ?? SHOULD I DO THIS ??
						//if (story.pubDate < DateTime.Now.AddMinutes(-_storyAge))
						//    continue;
						//
						// May be headline-only article, or a weird feed where the detail is much
						// shorter than the title (Quote of the day, title is quote, detail is author)
						//
						string time = story.pubDate.ToUniversalTime().ToString("HHmm") + "Z";
						XmlNode detNode = story.rssItem.SelectSingleNode("description");	// Try for description
						string detail;
						if (detNode != null)
							detail = GetMorseInputText(detNode.InnerText);
						else
							detail = "";

						string msg;
						if (M.Mode == Morse.CodeMode.International)				// Radiotelegraphy
						{
							msg = "NR " + _msgNr.ToString() + " DE RSS " + time + " \\BT\\";
							if (detail.Length < title.Length)
								msg += title + " " + detail;
							else
								msg += detail;
							msg += " \\AR\\";
						}
						else														// American telegraph
						{
							// TODO - Make time zone name adapt to station TZ and DST
							string date = story.pubDate.ToUniversalTime().ToString("MMM d h mm tt") + " GMT";
							msg = "NR " + _msgNr.ToString() + " RSS FILED " + date + " = ";
							if (detail.Length < title.Length)
								msg += title + " " + detail;
							else
								msg += detail;
							msg += " END";
						}
						_msgNr += 1;

						MorseMessage mMsg = new MorseMessage();
						mMsg.Title = title;
						mMsg.Contents = msg;
						messages.Add(mMsg);
					}

					if (messages.Count > 0)
					{
						//
						// Have message(s), generate Morse Code sound for each
						//
						int n = 1;
						foreach (MorseMessage msg in messages)
						{
							SetStatus("Sending message " + n++ + " of " + messages.Count);
							SetCrawler("");
							M.CwCom(msg.Contents, Send);
							lock (_titleCache)
							{
								_titleCache.Add(msg.Title, DateTime.Now);
							}
							Thread.Sleep(5000);
						}
					}
					else
					{
						//
						// No messages to send this time, wait until next time to poll
						//
						TimeSpan tWait = _lastPollTime.AddMinutes(_pollInterval) - DateTime.Now;
						if (tWait > TimeSpan.Zero)
						{
							for (int i = 0; i < tWait.TotalSeconds; i++)
							{
								string buf = TimeSpan.FromSeconds(tWait.TotalSeconds - i).ToString().Substring(3, 5);
								SetStatus("Check feed in " + buf + "...");
								Thread.Sleep(1000);
							}
						}
					}
					SetStatus("");
				}
			}
			catch (ThreadInterruptedException)
			{
				return;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "RSS to Morse", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
		}

	}
}
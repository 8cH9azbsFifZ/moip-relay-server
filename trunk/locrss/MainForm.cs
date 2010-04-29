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
// SOUND OUTPUT: This version uses System.Media.SoundPlayer for sound output
//				(see SpTones.cs and SpSounder.cs). TO change it to use Managed
//				DirectX, remove SpTones.cs and SpSounder.cs from the project,
//				add DxTones.cs and DxSounder.cs, then #define DIRECTX.
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
//
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Windows.Forms;
using System.Xml;

using com.dc3.morse;
using System.Diagnostics;

namespace com.dc3
{
	public partial class MainForm : Form
	{
		public enum CodeMode { International, American }
		public enum SoundMode { Tone, Sounder }
		//
		// Represents an RSS item with a correct pubDate
		//
		private class datedRssItem
		{
			public DateTime pubDate { get; set; }
			public XmlNode rssItem { get; set; }
		}
		
		//
		// Settings
		//
		private CodeMode _codeMode;
		private SoundMode _soundMode;
		private int _pollInterval;
		private int _codeSpeed;
		private int _toneFreq;
		private int _timingComp;
		private int _sounderNum;
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
#if DIRECTX
		private DxTones _tones;
		private DxSounder _sounder;
#else
		private SpTones _tones;
		private SpSounder _sounder;
#endif
		private DateTime _lastPollTime;
		private SerialPort _serialPort;

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
			_timingComp = (int)nudTimingComp.Value;
			_toneFreq = (int)nudToneFreq.Value;
			_sounderNum = (int)nudSounder.Value;
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
			if (Properties.Settings.Default.SoundMode == 0)
				rbTone.Checked = true;
			else
				rbSounder.Checked = true;

			foreach (string uri in Properties.Settings.Default.LRU)
				cbFeedUrl.Items.Add(uri);
			if (cbFeedUrl.Text == "")											// Force something into feed URL
				cbFeedUrl.Text = Properties.Settings.Default.LRU[0];

			_titleExpireThread = new Thread(new ThreadStart(TitleExpireThread));
			_titleExpireThread.Name = "Title Expiry";
			_titleExpireThread.Start();
#if DIRECTX
			_dxTones = new DxTones(this, 1000);
			_dxSounder = new DxSounder(this);
#else
			_tones = new SpTones(1000);
			_sounder = new SpSounder();
#endif
			_tones.Frequency = _toneFreq;
			_sounder.Sounder = _sounderNum;

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
		}

		private void nudTimingComp_ValueChanged(object sender, EventArgs e)
		{
			_timingComp = (int)nudTimingComp.Value;
		}
		private void nudToneFreq_ValueChanged(object sender, EventArgs e)
		{
			_toneFreq = (int)nudToneFreq.Value;
			_tones.Frequency = _toneFreq;
			_tones.Tone(100);
		}

		private void nudSounder_ValueChanged(object sender, EventArgs e)
		{
			_sounderNum = (int)nudSounder.Value;
			_sounder.Sounder = (int)nudSounder.Value;
			_sounder.ClickClack(100);
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
		}

		private void rbAmerican_CheckedChanged(object sender, EventArgs e)
		{
			if (rbAmerican.Checked)
			{
				_codeMode = CodeMode.American;
				Properties.Settings.Default.CodeMode = 1;
			}
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

		private void rbSounder_CheckedChanged(object sender, EventArgs e)
		{
			if (rbSounder.Checked)
			{
				_soundMode = SoundMode.Sounder;
				Properties.Settings.Default.SoundMode = 1;
			}
			UpdateUI();
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
#if MONO_BUILD
				SerialPort S = new SerialPort("/dev/tty.serial" + _serialPortNum.ToString());
#else
				SerialPort S = new SerialPort("COM" + _serialPortNum.ToString());
#endif
				S.Open();
				S.DtrEnable = true;
				for (int i = 0; i < 4; i++)
				{
					S.RtsEnable = true;
					Thread.Sleep(100);
					S.RtsEnable = false;
					Thread.Sleep(100);
				}
				S.DtrEnable = false;
				S.Close();
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

		private void llHelp_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			System.Diagnostics.Process.Start(Path.GetDirectoryName(Application.ExecutablePath) + "\\doc\\index.html");
		}

		private void picHelp_Click(object sender, EventArgs e)
		{
			System.Diagnostics.Process.Start(Path.GetDirectoryName(Application.ExecutablePath) + "\\doc\\index.html");
		}

		private void llRSSFeeds_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			System.Diagnostics.Process.Start("http://news.yahoo.com/page/rss");
		}

		private void picRSS_Click(object sender, EventArgs e)
		{
			System.Diagnostics.Process.Start("http://news.yahoo.com/page/rss");
		}

		private void UpdateUI()
		{
			bool enable = !_run;
			btnStartStop.Enabled = (cbFeedUrl.Text != "");
			cbFeedUrl.Enabled = enable;
			nudCodeSpeed.Enabled = enable;
			nudTimingComp.Enabled = enable;
			nudPollInterval.Enabled = enable;
			nudStoryAge.Enabled = enable;
			nudSerialPort.Enabled = enable;
			chkUseSerial.Enabled = enable;
			rbAmerican.Enabled = enable;
			rbInternational.Enabled = enable;
			picTestSerial.Enabled = enable & !chkUseSerial.Checked;

			enable = enable & !_useSerial;
			nudSounder.Enabled = enable & rbSounder.Checked;
			nudToneFreq.Enabled = enable & rbTone.Checked;
			rbTone.Enabled = enable;
			rbSounder.Enabled = enable;
		}

		private void btnStartStop_Click(object sender, EventArgs e)
		{
			if (!_run)
			{
				if (!cbFeedUrl.Items.Contains(cbFeedUrl.Text))					// Add new URIs to combo box when actually USED!
				{
					while (cbFeedUrl.Items.Count > 8)							// Safety catch only
						cbFeedUrl.Items.RemoveAt(0);
					if (cbFeedUrl.Items.Count == 8)								// If full, remove last item
						cbFeedUrl.Items.RemoveAt(7);
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
				// Try FeedBurner's EST
				//
				string buf = dateStr.Replace(" EST", "-0500");					// FeedBurner sends EST times :-(
				if (DateTime.TryParse(buf, out corrDate))
				{
					if (corrDate > DateTime.Now)								// If in future (e.g. Science News!)
						return DateTime.MinValue;
					else
						return corrDate;
				}
				else
				{
					return DateTime.MinValue;									// [sentinel]
				}
			}
			else																// Converted successfully!
				return corrDate;
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
						Thread.Sleep(code[i]);
						_serialPort.RtsEnable = false;
					}
					else
					{
						if (_soundMode == SoundMode.Tone)
							_tones.Tone(code[i]);
						else
							_sounder.ClickClack(code[i]);
					}
				}
				else
					Thread.Sleep(_useSerial ? -code[i] : -code[i] - _timingComp);
			}
			string ct = Regex.Replace(text, "\\s", " ");
			AddToCrawler(ct);
		}

		private void Run()
		{
			try
			{
				Morse M = new Morse();
				M.CharacterWpm = _codeSpeed;
				M.WordWpm = _codeSpeed;
				M.Mode = (_codeMode == CodeMode.International ? Morse.CodeMode.International : Morse.CodeMode.American);

				_msgNr = 1;															// Start with message #1

				if (_useSerial)
				{
#if MONO_BUILD
					_serialPort = new SerialPort("/dev/tty.serial" + _serialPortNum.ToString());
#else
					_serialPort = new SerialPort("COM" + _serialPortNum.ToString());
#endif
					_serialPort.Open();
					_serialPort.DtrEnable = true;
				}

				lock (_titleCache) { _titleCache.Clear(); }							// Clear title cache on start

				while (true)
				{
					_lastPollTime = DateTime.Now;

					SetStatus("Getting RSS feed data...");
					XmlDocument feedXml = new XmlDocument();
					feedXml.Load(_feedUrl);

					XmlNodeList items = feedXml.SelectNodes("/rss/channel/item");
					if (items.Count == 0)
						throw new ApplicationException("This does not look like an RSS feed");

					List<datedRssItem> stories = new List<datedRssItem>();
					foreach (XmlNode item in items)
					{
						DateTime pubUtc = GetPubDateUtc(item);						// See comments above, sick hack
						if (pubUtc == DateTime.MinValue)
							continue;												// Bad pubDate, skip this story
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
					List<string> messages = new List<string>();
					foreach (datedRssItem story in stories)
					{
						string title = GetMorseInputText(story.rssItem.SelectSingleNode("title").InnerText);
						lock (_titleCache)
						{
							if (_titleCache.ContainsKey(title))
								continue;												// Recently sent, skip
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
						if (M.Mode == Morse.CodeMode.International)					// Radiotelegraphy
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

						lock (_titleCache)
						{
							_titleCache.Add(title, DateTime.Now);
						}

						messages.Add(msg);
					}

					if (messages.Count > 0)
					{
						//
						// Have message(s), generate Morse Code sound for each
						//
						int n = 1;
						foreach (string msg in messages)
						{
							SetStatus("Sending message " + n++ + " of " + messages.Count);
							SetCrawler("");
							M.CwCom(msg, Send);
							Thread.Sleep(5000);
						}
					}
					else
					{
						//
						// NJo messages to send this time, wait until next time to poll
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
﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
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
		private int _pollInterval;
		private int _codeSpeed;
		private int _toneFreq;
		private int _storyAge;
		private string _feedUrl;

		//
		// State variables
		//
		private Thread _runThread = null;
		private bool _run;
		private Dictionary<string, DateTime> _titleCache = new Dictionary<string, DateTime>();
		private Thread _titleExpireThread = null;
		private int _msgNr = 1;
		private DxTones _dxTones;
		private DateTime _lastPollTime;

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
			_toneFreq = (int)nudToneFreq.Value;
			_storyAge = (int)nudStoryAge.Value;
			_feedUrl = txtFeedUrl.Text;
			_run = false;

			if (Properties.Settings.Default.CodeMode == 0)
				rbInternational.Checked = true;									// Triggers CheckedChanged (typ.)
			else
				rbAmerican.Checked = true;

			_titleExpireThread = new Thread(new ThreadStart(TitleExpire));
			_titleExpireThread.Name = "Title Expiry";
			_titleExpireThread.Start();
			_dxTones = new DxTones(this, 1000);
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
			Properties.Settings.Default.Save();
		}

		private void txtFeedUrl_TextChanged(object sender, EventArgs e)
		{
			_feedUrl = txtFeedUrl.Text;
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

		private void nudToneFreq_ValueChanged(object sender, EventArgs e)
		{
			_toneFreq = (int)nudToneFreq.Value;
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

		private void EnableControls(bool enable)
		{
			txtFeedUrl.Enabled = enable;
			nudCodeSpeed.Enabled = enable;
			nudPollInterval.Enabled = enable;
			nudStoryAge.Enabled = enable;
			nudToneFreq.Enabled = enable;
			rbAmerican.Enabled = enable;
			rbInternational.Enabled = enable;
		}

		private void btnStartStop_Click(object sender, EventArgs e)
		{
			if (!_run)
			{
				_runThread = new Thread(new ThreadStart(Run));
				_runThread.Name = "RSS engine";
				_runThread.Start();
				EnableControls(false);
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
				EnableControls(true);
				btnStartStop.Text = "Start";
				_run = false;
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
		private void TitleExpire()
		{
			try
			{
				while (true)
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
					Thread.Sleep(900000);										// Every 15 min
				}
			}
			catch (ThreadInterruptedException)
			{
				return;
			}
		}

		//
		// Sender delegate for the CwCom mode of Morse. This gets timing arrays, and calls
		// the tone generator (it's not really CwCom :-)).
		//
		private void Send(Int32[] code, string text)
		{
			for (int i = 0; i < code.Length; i++)
			{
				if (code[i] > 0)
					_dxTones.Tone(code[i], true);
				else
					Thread.Sleep(-code[i]);
			}
		}

		private void Run()
		{
			try
			{
				Morse M = new Morse();
				M.CharacterWpm = _codeSpeed;
				M.WordWpm = _codeSpeed;
				M.Mode = (_codeMode == CodeMode.International ? Morse.CodeMode.International : Morse.CodeMode.American);

				lock (_titleCache) { _titleCache.Clear(); }							// Clear title cache on start

				while (true)
				{
					_lastPollTime = DateTime.Now;

					statBarLabel.Text = "Getting RSS feed data...";
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
						datedRssItem n = new datedRssItem();
						n.pubDate = pubUtc;
						n.rssItem = item;
						stories.Add(n);
					}

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
							msg = "DE RSS " + time + " \\BT\\ ";
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
							msg = _msgNr.ToString() + " RSS FILED " + date + " = ";
							_msgNr += 1;
							if (detail.Length < title.Length)
								msg += title + " " + detail;
							else
								msg += detail;
							msg += " END";
						}

						lock (_titleCache)
						{
							_titleCache.Add(title, DateTime.Now);
						}

						messages.Add(msg);
					}

					//
					// Now generate Morse Code sound for each of the messages in ths list
					//
					foreach (string msg in messages)
					{
						int n = 1;
						statBarLabel.Text = "Sending message " + n + " of " + messages.Count;
						M.CwCom(msg, Send);
						Thread.Sleep(5000);
					}

					//
					// Wait until next time to poll
					//
					TimeSpan tWait = _lastPollTime.AddMinutes(_pollInterval) - DateTime.Now;
					if (tWait > TimeSpan.Zero)
					{
						for (int i = 0; i < tWait.TotalSeconds; i++)
						{
							string buf = TimeSpan.FromSeconds(i).ToString().Substring(3);
							statBarLabel.Text = "Check feed in " + buf + "...";
							Thread.Sleep(1000);
						}
					}
					statBarLabel.Text = "";
				}


			}
			catch (ThreadInterruptedException)
			{
				return;
			}
			catch (Exception ex)
			{
				MessageBox.Show(null, ex.Message, "RSS to Morse", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
		}

	}
}
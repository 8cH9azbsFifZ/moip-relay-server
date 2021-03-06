﻿//tabs=4
//-----------------------------------------------------------------------------
// TITLE:		frmMain.cs
//
// FACILITY:	Morse Code Keyer
//
// ABSTRACT:	Program that will read either the left/right mouse buttons or 
//				a key attached to a serial port's CTS/DSR pins and output CW 
//				tones or telegraph sounder sounds. It will also drive the 
//				DTR pin on the serial port for external telegraph loop or
//				ham rig. Supports manual/straight keying, semi-auto (bug)
//				keying, and iambic A/B mode automatic keying.
//
// IMPORTANT:	The Sender delegate must be synchronous, that is, it must not
//				return until the symbol is actually sent (tone played, etc.).
//
// NOTE:		The .NET SerialPort class is too sluggish for high speed keyer
//				operation. THe NEW_COM #define uses code to call the ComPortCtrl
//				class, which uses low-level P/Invoke code to talk to the kernel
//				serial I/O driver.
//
// ENVIRONMENT:	Microsoft.NET 2.0/3.5
//				Developed under Visual Studio.NET 2008
//
//
// AUTHOR:		Bob Denny, <rdenny@dc3.com>
//
// Edit Log:
//
// When			Who		What
//----------	---		-------------------------------------------------------
// ??-Apr-10	rbd		Initial development
// 07-May-10	rbd		New ComPortCtrl class for low-level serial I/O
// 07-May-10	rbd		Refactored IambicKeyer into a separate assembly. Other
//						tweaks.
// 11-May-10	rbd		1.1.0 - Volume Control!
// 17-May-10	rbd		1.1.0 - Flashing mode B indicator light
// 18-May-10	rbd		1.1.0 - Always send via serial port if "Use" checked.
//						Fix serial sending in straight-key mode. Add new mode
//						Semi-Auto (bug). Remove Physical Sounder sound mode, 
//						can just turn volume down to 0 if don't want sound.
//						Make slider value 0 mean no sound at all. Refactoring
//						of sound logic.
// 19-May-10	rbd		More refactoring of sound logic. Add lock to ensure
//						competion of dit, including trailing space, before 
//						(manual) dah in semi-auto mode, and force inter-
//						symbol space after manual dah in semi-auto.
// 21-May-10	rbd		1.5.0 - Match version with RSSMorse, these will be 
//						combined into a single installer. Change doc root
//						from index.html to keyer.html so can share same doc
//						folder in end-user install.
// 02-Jun-11	rbd		1.8.0 - DXTones now use StartLatency for envelope
//						rise/fall. Set a nice (fixed) falue for keying.
// 25-Jun-11	rbd		2.1.0 - Match version with Morse News, increase max
//						speed to 60 WPM (ridiculous but...). 
// 28-Nov-11	rbd		2.2.0 (SF #3432844) Add logic for sound device sel.
//						(SF #3444486) Add tone envelope rise/fall time control.
// 19-Apr-12	rbd		2.4.0 - Uprev version to match Morse News, no changes.
// 30-Apr-12	rbd		2.5.0 - SF #3460283 support COM ports > 9 per 
//								http://support.microsoft.com/kb/115831
// 20-May-12	rbd		2.5.0 - SF #3522625 ASIO support
// 11-Jun-12	rbd		3.0.0 - Rename StartLatency to RiseFallTime in sound 
//						interfaces to reflect its true purpose.
// 24-Dec-2012	rbd		3.1.1 -	Do not save window positions unless "normal" and fix up old
//								saved window positions if minimized (-32000).
//

#define NEW_COM								// Define to use P/Invoke serial port I/O

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.IO;
using Microsoft.DirectX.DirectSound;
using BlueWave.Interop.Asio;

namespace com.dc3.morse
{
	public partial class frmMain : Form
	{
		//
		// Constants
		//
		private enum SoundDevType
		{
			DirectX = 0,
			ASIO = 1
		}
		private const int _tbase = 1200;	// Time base for millisecond timings. A standard 1WPM dit = 1200 ms mark, 1200 ms space

		//
		// Config
		//
		private int _keyerMode;				// Keyer mode, 0=straight 1=iambic
		private int _soundMode;				// Sound output, 0=tone 1=sounder
		private int _toneFreq;
		private int _sounderNum;
		private int _codeSpeed;				// Character WPM
		private int _riseFallMs;
		private int _serialPortNum;
		private bool _useSerial;
		private bool _iambicA;
		private bool _swapPaddles;

		//
		// State Variables
		//
#if NEW_COM
		private ComPortCtrl _serialPort;
#else
		private SerialPort _serialPort;		// Used for paddle via serial and optional real sounder output
#endif
		private SoundDev _selSoundDev;
		private DxTones _dxTones = null;			// [senitnel]
		private DxSounder _dxSounder = null;		// [sentinel]
		private AsioTones _asioTones = null;		// [sentinel]
		private IambicKeyer _iambicKeyer = null;	// [sentinel]
		private int _ctime;					// Character dit time (ms)
		private int _stime;					// Symbol space time (ms)
		private int _cstime;				// Character space time (ms)
		private int _wstime;				// Word space time (ms)
		private object _semiAutoLock;
		private object _serialPortLock;

		private struct SoundDev
		{
			public SoundDevType type;
			public DeviceInformation info;
			public InstalledDriver asio;
			public override string ToString()		// Allow display in list
			{
				if (type == SoundDevType.DirectX)
					return info.Description;
				else
					return asio.Name + " (ASIO)";
			}
			public SoundDev(DeviceInformation di) { type = SoundDevType.DirectX; info = di; asio = null; }
			public SoundDev(InstalledDriver dv) { type = SoundDevType.ASIO; asio = dv; info = new DeviceInformation(); }
		}

		//
		// Form construction, load, closing
		//
		public frmMain()
		{
			InitializeComponent();
		}

		private void _calcSpaceTime()					// Calculate space times for Farnsworth (word rate < char rate)
		{
			//
			// There are 50 units in "PARIS " - 36 are in the characters, 14 are in the spaces
			//
			int t_total = (_tbase / _codeSpeed) * 50;
			int t_chars = (_tbase / _codeSpeed) * 36;
			_stime = (t_total - t_chars) / 14;			// Time for 1 space (ms)
			_cstime = _stime * 2;						// Character and word spacing
			_wstime = _stime * 4;
		}

		private void frmMain_Load(object sender, EventArgs e)
		{
			//
			// These must precede the sound device selection, as they
			// are used to set up sound after the device is selected
			// in the listbox event below.
			//
			_toneFreq = (int)nudToneFreq.Value;
			_sounderNum = (int)nudSounder.Value;
			_codeSpeed = (int)Properties.Settings.Default.CodeSpeed;
			_ctime = _tbase / _codeSpeed;
			_calcSpaceTime();
			_riseFallMs = (int)Properties.Settings.Default.RiseFall;
			//
			// Set up the sound device selector and select the one saved
			// in the app settings. This has the side effect of setting
			// up the sound generators.
			//
			SoundDevType soundDevType;
			string soundDevGUID;	
			if (Properties.Settings.Default.SoundDevType == "DX")
				soundDevType = SoundDevType.DirectX;
			else
				soundDevType = SoundDevType.ASIO;
			soundDevGUID = (string)Properties.Settings.Default.SoundDevGUID;	// Get the current GUID(DX)/CLSID(ASIO) (or blank)
			//
			// Populate the sound device droplist and detect which one 
			// was saved in the registry from last time (if any);
			//
			DevicesCollection myDevices = new DevicesCollection();				// First the DirectX devices
			int iSel = -1;
			int i = 0;
			foreach (DeviceInformation info in myDevices)
			{
				SoundDev sd = new SoundDev(info);
				cbSoundDevs.Items.Add(sd);
				if (soundDevType == SoundDevType.DirectX && info.DriverGuid.ToString() == soundDevGUID)
					iSel = i;
				i += 1;
			}

			for (int j = 0; j < AsioDriver.InstalledDrivers.Length; j++)		// Then any ASIO devices
			{
				SoundDev sd = new SoundDev(AsioDriver.InstalledDrivers[j]);
				cbSoundDevs.Items.Add(sd);
				if (soundDevType == SoundDevType.ASIO && AsioDriver.InstalledDrivers[j].ClsId == soundDevGUID)
					iSel = i;
				i += 1;
			}
			cbSoundDevs.SelectedIndex = iSel;									// THIS FIRES UP THE SOUND SYSTEM

			_semiAutoLock = new object();
			_serialPortLock = new object();

			_keyerMode = Properties.Settings.Default.KeyerMode;
			if (_keyerMode == 0)
				rbStraightKey.Checked = true;
			else if (_keyerMode == 1)
				rbBug.Checked = true;
			else
				rbIambic.Checked = true;

			_iambicA = chkModeA.Checked;
			_swapPaddles = chkSwapPaddles.Checked;

			_soundMode = Properties.Settings.Default.SoundMode;
			if (_soundMode == 0)
				rbTone.Checked = true;
			else
				rbSounder.Checked = true;

			_serialPortNum = (int)nudSerialPort.Value;
			_useSerial = chkUseSerial.Checked;
			if (_useSerial)
			{
				if (!OpenSerialPort())
					chkUseSerial.Checked = false;
			}

			_iambicKeyer = new IambicKeyer(SendCallback);
			_iambicKeyer.ModeB = !_iambicA;

			this.Left = Properties.Settings.Default.SavedWinX;					// TODO safety these
			this.Top = Properties.Settings.Default.SavedWinY;
			if (this.WindowState != FormWindowState.Normal || this.Left <= 0 || this.Top <= 0)	// Fix up old saved minimized coordinates
			{
				this.Left = 100;
				this.Top = 80;
				this.WindowState = FormWindowState.Normal;
			}
		}

		private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (_asioTones != null)
				_asioTones.Dispose();
			if (_dxTones != null)
				_dxTones.Dispose();
			if (_dxSounder != null)
				_dxSounder.Dispose();
			if (this.WindowState == FormWindowState.Normal && this.Left > 0 && this.Top > 0) // Don't save position if minimized
			{
				Properties.Settings.Default.SavedWinX = this.Left;
				Properties.Settings.Default.SavedWinY = this.Top;
			}
			Properties.Settings.Default.SoundDevGUID = ((SoundDev)cbSoundDevs.SelectedItem).info.DriverGuid.ToString();
			if (_selSoundDev.type == SoundDevType.DirectX)
			{
				Properties.Settings.Default.SoundDevGUID = _selSoundDev.info.DriverGuid.ToString();
				Properties.Settings.Default.SoundDevType = "DX";
			}
			else
			{
				Properties.Settings.Default.SoundDevGUID = _selSoundDev.asio.ClsId.ToString();
				Properties.Settings.Default.SoundDevType = "ASIO";
			}
			Properties.Settings.Default.Save();
			if (_serialPort != null) _serialPort.Close();
			_iambicKeyer.Dispose();
		}

		//
		// Control events
		//

		//
		// This gets fired during initialization as well, so it will start
		// up the sound system that was saved the last time the app was run.
		//
		private void cbSoundDevs_SelectedIndexChanged(object sender, EventArgs e)
		{
			_selSoundDev = (SoundDev)cbSoundDevs.SelectedItem;
			if (_selSoundDev.type == SoundDevType.DirectX)
			{
				Guid drvrGuid = ((SoundDev)cbSoundDevs.SelectedItem).info.DriverGuid;

				if (_asioTones != null)
					_asioTones.Dispose();
				_asioTones = null;
				if (_dxTones != null)
					_dxTones.Dispose();
				_dxTones = new DxTones(this, drvrGuid, 1000);
				_dxTones.Frequency = _toneFreq;
				_dxTones.DitMilliseconds = _ctime;
				_dxTones.Volume = tbVolume.Value / 10.0F;
				_dxTones.RiseFallTime = _riseFallMs;

				if (_dxSounder != null)
					_dxSounder.Dispose();
				_dxSounder = new DxSounder(this, drvrGuid);
				_dxSounder.SoundIndex = _sounderNum;
				_dxSounder.DitMilliseconds = _ctime;
				_dxSounder.Volume = tbVolume.Value / 10.0F;
			}
			else
			{
				if (_dxTones != null)
					_dxTones.Dispose();
				_dxTones = null;
				if (_asioTones != null)
					_asioTones.Dispose();
				_asioTones = new AsioTones(AsioDriver.SelectDriver(_selSoundDev.asio), 1000);
				_asioTones.Frequency = _toneFreq;
				_asioTones.DitMilliseconds = _ctime;
				_asioTones.Volume = tbVolume.Value / 10.0F;
				_asioTones.RiseFallTime = _riseFallMs;					// Actually rise/fall time
				_asioTones.Start();										// Make it run now!

				if (_dxSounder != null)
					_dxSounder.Dispose();
				_dxSounder = null;										// No ASIO sounder stuff.
			}
			UpdateUI();
		}

		private void nudSpeed_ValueChanged(object sender, EventArgs e)
		{
			_codeSpeed = (int)nudCodeSpeed.Value;
			_ctime = _tbase / _codeSpeed;
			if (_selSoundDev.type == SoundDevType.DirectX)
			{
				_dxTones.DitMilliseconds = _ctime;
				_dxSounder.DitMilliseconds = _ctime;
			}
			else
			{
				_asioTones.DitMilliseconds = _ctime;
			}
			_calcSpaceTime();
		}

		private void nudToneFreq_ValueChanged(object sender, EventArgs e)
		{
			_toneFreq = (int)nudToneFreq.Value;
			if (_selSoundDev.type == SoundDevType.DirectX)
			{
				_dxTones.Frequency = _toneFreq;
				_dxTones.PlayFor(100);
			}
			else
			{
				_asioTones.Frequency = _toneFreq;
				_asioTones.PlayFor(100);
			}
		}

		private void nudRiseFallMs_ValueChanged(object sender, EventArgs e)
		{
			_riseFallMs = (int)nudRiseFallMs.Value;
			if (_selSoundDev.type == SoundDevType.DirectX)
			{
				_dxTones.RiseFallTime = _riseFallMs;
				_dxTones.PlayFor(100);
			}
			else
			{
				_asioTones.RiseFallTime = _riseFallMs;
				_asioTones.PlayFor(100);
			}
		}

		private void nudSounder_ValueChanged(object sender, EventArgs e)
		{
			_sounderNum = (int)nudSounder.Value;
			_dxSounder.SoundIndex = _sounderNum;
			_dxSounder.PlayFor(100);
		}

		private void rbTone_CheckedChanged(object sender, EventArgs e)
		{
			if (rbTone.Checked)
			{
				_soundMode = 0;
				Properties.Settings.Default.SoundMode = 0;
			}
			UpdateUI();
		}

		private void rbSounder_CheckedChanged(object sender, EventArgs e)
		{
			if (rbSounder.Checked)
			{
				_soundMode = 1;
				Properties.Settings.Default.SoundMode = 1;
			}
			UpdateUI();
		}

		private void rbStraightKey_CheckedChanged(object sender, EventArgs e)
		{
			if (rbStraightKey.Checked)
			{
				_keyerMode = 0;
				Properties.Settings.Default.KeyerMode = 0;
			}
			UpdateUI();
		}

		private void rbBug_CheckedChanged(object sender, EventArgs e)
		{
			if (rbBug.Checked)
			{
				_keyerMode = 1;
				Properties.Settings.Default.KeyerMode = 1;
			}
			UpdateUI();
		}

		private void rbIambic_CheckedChanged(object sender, EventArgs e)
		{
			if (rbIambic.Checked)
			{
				_keyerMode = 2;
				Properties.Settings.Default.KeyerMode = 2;
			}
			UpdateUI();
		}

		private void chkModeA_CheckedChanged(object sender, EventArgs e)
		{
			_iambicA = chkModeA.Checked;
			if (_iambicKeyer != null) _iambicKeyer.ModeB = !_iambicA;
			UpdateUI();
		}

		private void chkSwapPaddles_CheckedChanged(object sender, EventArgs e)
		{
			_swapPaddles = chkSwapPaddles.Checked;
			UpdateUI();
		}

		private void tbVolume_Scroll(object sender, EventArgs e)
		{
			if (_selSoundDev.type == SoundDevType.DirectX)
			{
				_dxTones.Volume = _dxSounder.Volume = tbVolume.Value / 10.0F;
				if (_soundMode == 0)
					_dxTones.PlayFor(60);
				else if (_soundMode == 1)
					_dxSounder.PlayFor(60);
			}
			else
			{
				_asioTones.Volume = tbVolume.Value / 10.0F;
				if (_soundMode == 0)
					_asioTones.PlayFor(60);
				// TODO No sounder support in ASIO
			}
		}

		private void llHelp_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			picHelp_Click(sender, new EventArgs());
		}

		private void picHelp_Click(object sender, EventArgs e)
		{
			System.Diagnostics.Process.Start(Path.GetDirectoryName(Application.ExecutablePath) + "\\doc\\keyer.html");
		}

		private void nudSerialPort_ValueChanged(object sender, EventArgs e)
		{
			_serialPortNum = (int)nudSerialPort.Value;
		}

		private void chkUseSerial_CheckedChanged(object sender, EventArgs e)
		{
			_useSerial = chkUseSerial.Checked;
			if (_serialPort != null)
			{
				_serialPort.Close();
				_serialPort = null;
			}
			if (_useSerial) OpenSerialPort();
			UpdateUI();
		}

		private void btnTestSerial_Click(object sender, EventArgs e)
		{
			btnTestSerial.Enabled = false;
			if (OpenSerialPort())
			{
				for (int i = 0; i < 4; i++)
				{
					_serialPort.RtsEnable = true;
					PreciseDelay.Wait(100);
					_serialPort.RtsEnable = false;
					PreciseDelay.Wait(100);
				}
				_serialPort.Close();
				_serialPort = null;
				MessageBox.Show(null, "Test complete, 4 dits sent.", "Sounder Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			btnTestSerial.Enabled = true;
		}

		//
		// Common functions for mouse and serial keying inputs
		//

		//
		// These two start and end manual sounds (straight key and semi-suto dahs)
		//
		private void StartSound()
		{
			_iambicKeyer.KeyEvent(IambicKeyer.KeyEventType.DitRelease);			// Force semi auto dits to stop before locking!
			lock (_semiAutoLock)
			{
				if (_serialPort != null) _serialPort.RtsEnable = true;
				if (_selSoundDev.type == SoundDevType.DirectX)
				{
					if (_soundMode == 0)
						_dxTones.Down();
					else
						_dxSounder.Down();
				}
				else
				{
					if (_soundMode == 0)
						_asioTones.Down();
					// TODO No sounder support in ASIO
				}
			}
		}

		private void EndSound()
		{
			lock (_semiAutoLock)
			{
				if (_serialPort != null) _serialPort.RtsEnable = false;
				if (_selSoundDev.type == SoundDevType.DirectX)
				{
					if (_soundMode == 0)
						_dxTones.Up();
					else
						_dxSounder.Up();
				}
				else
				{
					if (_soundMode == 0)
						_asioTones.Up();
					// TODO No sounder support in ASIO
				}
				if (_keyerMode == 1) PreciseDelay.Wait(_ctime);					// Force inter-symbol space after (manual) Dah
			}
		}

		//
		// These four handle left and right mouse and (serial) paddle/swiper/key presses
		//
		private void LeftDown()
		{
			if (_keyerMode == 0)
			{
				StartSound();
				return;															// FINISHED
			}
			if (_swapPaddles)
			{
				if (_keyerMode == 1)
					StartSound();
				else
					_iambicKeyer.KeyEvent(IambicKeyer.KeyEventType.DahPress);
			}
			else
				_iambicKeyer.KeyEvent(IambicKeyer.KeyEventType.DitPress);
		}

		private void LeftUp()
		{
			if (_keyerMode == 0)
			{
				EndSound();
				return;															// FINISHED
			}
			if (_swapPaddles)
			{
				if (_keyerMode == 1)
					EndSound();
				else
					_iambicKeyer.KeyEvent(IambicKeyer.KeyEventType.DahRelease);
			}
			else
				_iambicKeyer.KeyEvent(IambicKeyer.KeyEventType.DitRelease);
		}

		private void RightDown()
		{
			if (_keyerMode == 0)
			{
				StartSound();
				return;															// FINISHED
			}
			if (!_swapPaddles)
			{
				if (_keyerMode == 1)
					StartSound();
				else
					_iambicKeyer.KeyEvent(IambicKeyer.KeyEventType.DahPress);
			}
			else
				_iambicKeyer.KeyEvent(IambicKeyer.KeyEventType.DitPress);
		}

		private void RightUp()
		{
			if (_keyerMode == 0)
			{
				EndSound();
				return;															// FINISHED
			}
			if (!_swapPaddles)
			{
				if (_keyerMode == 1)
					EndSound();
				else
					_iambicKeyer.KeyEvent(IambicKeyer.KeyEventType.DahRelease);
			}
			else
				_iambicKeyer.KeyEvent(IambicKeyer.KeyEventType.DitRelease);
		}

		private void pnlHotSpot_MouseDown(object sender, MouseEventArgs e)
		{
			switch (e.Button)
			{
				case MouseButtons.Left:
					LeftDown();
					break;

				case MouseButtons.Right:
					RightDown();
					break;
			}
		}

		private void pnlHotSpot_MouseUp(object sender, MouseEventArgs e)
		{
			switch (e.Button)
			{
				case MouseButtons.Left:
					LeftUp();
					break;

				case MouseButtons.Right:
					RightUp();
					break;
			}
		}

		private bool OpenSerialPort()
		{
			try
			{
#if NEW_COM
				_serialPort = new ComPortCtrl();
				_serialPort.ComPortPinChanged += new ComPortEventHandler(comPort_PinChanged);
				_serialPort.Open("\\\\.\\COM" + _serialPortNum.ToString());
				_serialPort.DtrEnable = true;					// TODO is this needed? It was with the .NET SerialPort class

#else
				_serialPort = new SerialPort("\\\\.\\COM" + _serialPortNum.ToString());
				_serialPort.PinChanged += new SerialPinChangedEventHandler(comPort_PinChanged);
				_serialPort.Open();
				_serialPort.DtrEnable = true;					// Not sure why, but these are needed to get pin events
#endif
				return true;
			}
			catch (Exception ex)
			{
				_serialPort.Close();
				_serialPort = null;
				MessageBox.Show(this, ex.Message, "Serial Port", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				chkUseSerial.Checked = false;
				return false;
			}
		}


		private void UpdateUI()
		{
			nudCodeSpeed.Enabled = chkSwapPaddles.Enabled = (rbIambic.Checked || rbBug.Checked);
			chkModeA.Enabled = rbIambic.Checked;
			pnlModeB.Visible = rbIambic.Checked && !chkModeA.Checked;
			nudToneFreq.Enabled = rbTone.Checked;
			nudRiseFallMs.Enabled = rbTone.Checked;
			nudSounder.Enabled = rbSounder.Checked;
			btnTestSerial.Enabled = nudSerialPort.Enabled = !chkUseSerial.Checked;
			if (_soundMode == 1 && _selSoundDev.type == SoundDevType.ASIO)
			{
				rbTone.Checked = true;
				rbSounder.Enabled = false;						// No sounder with ASIO
			}
			else
			{
				rbSounder.Enabled = true;
			}

		}

		static bool _prevDSR = false;
		static bool _prevCTS = false;

		//
		// TODO need real debouncing! 
		//
#if NEW_COM
		private void comPort_PinChanged(Object sender, ComPortEventArgs e)
		{
			ComPortCtrl com = (ComPortCtrl)sender;
			bool curState;
#else
		private void comPort_PinChanged(Object sender, SerialPinChangedEventArgs e)
		{
			SerialPort com = (SerialPort)sender;
#endif
			switch (e.EventType)
			{
#if NEW_COM
				case ComPortEventArgs.PinChange.DsrChanged:
#else
				case SerialPinChange.DsrChanged:
#endif
					lock (_serialPortLock) { curState = com.DsrHolding; }
					//Debug.Print("DSR -> " + curState.ToString() + " (prev = " + _prevDSR.ToString() + ")");
					if (curState == _prevDSR) return;							// Simple debouncing (typ.)
					if (curState)
						LeftDown();
					else
						LeftUp();
					_prevDSR = curState;
					break;

#if NEW_COM
				case ComPortEventArgs.PinChange.CtsChanged:
#else
				case SerialPinChange.CtsChanged:
#endif
					lock (_serialPortLock) { curState = com.CtsHolding; }
					//Debug.Print("CTS -> " + curState.ToString() + " (prev = " + _prevCTS.ToString() + ")");
					if (curState == _prevCTS) return;
					if (curState)
						RightDown();
					else
						RightUp();
					_prevCTS = curState;
					break;
			}
			//com.BreakState = true;
		}

		private void SendCallback(IambicKeyer.MorseSymbol S)
		{
			lock (_semiAutoLock)
			{
				ITone Tone;
				IAudioWav Sounder;
				if (_selSoundDev.type == SoundDevType.DirectX)
				{
					Tone = _dxTones;
					Sounder = _dxSounder;
				}
				else
				{
					Tone = _asioTones;
					Sounder = null;
					if (_soundMode == 1) return;
				}

				//Debug.Print(DateTime.Now.Ticks.ToString() + " " + S.ToString());
				if (S == IambicKeyer.MorseSymbol.Dit || S == IambicKeyer.MorseSymbol.DitB)
				{
					if (S == IambicKeyer.MorseSymbol.DitB)
						pnlModeB.BackColor = Color.Yellow;
					switch (_soundMode)
					{
						case 0:
							if (_serialPort != null) _serialPort.RtsEnable = true;
							Tone.Dit();
							if (_serialPort != null) _serialPort.RtsEnable = false;
							Tone.Space();
							break;
						case 1:
							if (_serialPort != null) _serialPort.RtsEnable = true;
							Sounder.Dit();
							if (_serialPort != null) _serialPort.RtsEnable = false;
							Sounder.Space();
							break;
					}
					if (S == IambicKeyer.MorseSymbol.DitB)
						pnlModeB.BackColor = Color.Black;
				}
				else // Dah or DahB
				{
					if (S == IambicKeyer.MorseSymbol.DahB)
						pnlModeB.BackColor = Color.Yellow;
					switch (_soundMode)
					{
						case 0:
							if (_serialPort != null) _serialPort.RtsEnable = true;
							Tone.Dah();
							if (_serialPort != null) _serialPort.RtsEnable = false;
							Tone.Space();
							break;
						case 1:
							if (_serialPort != null) _serialPort.RtsEnable = true;
							Sounder.Dah();
							if (_serialPort != null) _serialPort.RtsEnable = false;
							Sounder.Space();
							break;
					}
					if (S == IambicKeyer.MorseSymbol.DahB)
						pnlModeB.BackColor = Color.Black;
				}
			}
		}

	}
}

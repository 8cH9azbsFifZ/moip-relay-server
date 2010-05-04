//tabs=4
//-----------------------------------------------------------------------------
// TITLE:		DxSounder.cs
//
// FACILITY:	RSS to Morse tool
//
// ABSTRACT:	Generates telegraph sounder sounds via Managed DirectX. 
//
// ENVIRONMENT:	Microsoft.NET 2.0/3.5
//				Developed under Visual Studio.NET 2008
//				Also may be built under MonoDevelop 2.2.1/Mono 2.4+
//
// AUTHOR:		Bob Denny, <rdenny@dc3.com>
//
// Edit Log:
//
// When			Who		What
//----------	---		-------------------------------------------------------
// 22-Apr-10	rbd		From DxTones, for sounder audio
// 30-Apr-10	rbd		1.2.0 - Resurrect, simplify sound resource loading
//						ISounder.
// 02-May-10	rbd		Interface and ctor changes for loadable directx classes
// 03-May-10	rbd		1.3.0 - No loadables. Shipping DX assys. Refactor to new
//						common IAudioWav interface.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.DirectX.DirectSound;

namespace com.dc3.morse
{
    class DxSounder : IAudioWav
    {
        private Device _deviceSound;
		private int _sounder;
		private int _ditMs;
		private int _startLatency;

		private BufferDescription _bufDescClick;
		private BufferDescription _bufDescClack;
		private Microsoft.DirectX.DirectSound.Buffer _bufClick;
		private Microsoft.DirectX.DirectSound.Buffer _bufClack;

		public DxSounder(System.Windows.Forms.Control Handle)
		{
			_ditMs = 80;

			_deviceSound = new Microsoft.DirectX.DirectSound.Device();
			_deviceSound.SetCooperativeLevel(Handle, CooperativeLevel.Priority);	// Up priority for quick response

			_bufDescClick = new BufferDescription();
			_bufDescClick.ControlEffects = false;								// Necessary because .wav file is so short (typ.)
			_bufDescClick.GlobalFocus = true;									// Enable audio when program is in background (typ.)

			_bufDescClack = new BufferDescription();
			_bufDescClack.ControlEffects = false;
			_bufDescClack.GlobalFocus = true;

			this.SoundIndex = 1;													// Default to sounder #1
		}
		
		//
		// Publics
		//
		public int SoundIndex
		{
			get { return _sounder; }
			set
			{
				if (value < 1 || value > 7)
					throw new ApplicationException("Sounder number out of range");
				_sounder = value;
				_bufClick = new Microsoft.DirectX.DirectSound.Buffer(Properties.Resources.ResourceManager.GetStream("Click_" + value), 
							_bufDescClick, _deviceSound);
				_bufClack = new Microsoft.DirectX.DirectSound.Buffer(Properties.Resources.ResourceManager.GetStream("Clack_" + value), 
							_bufDescClick, _deviceSound);
			}
		}

		public int StartLatency
		{
			get { return _startLatency; }
			set { _startLatency = value; }
		}

		public int DitMilliseconds
		{
			get { return _ditMs; }
			set { _ditMs = value; }
		}

		public void Dit()
		{
			PlayFor(_ditMs);
		}

		public void Dah()
		{
			PlayFor(_ditMs * 3);
		}

		public void Space()
		{
//			Thread.Sleep(_ditMs - _startLatency);
			PreciseDelay.Wait(_ditMs - _startLatency);
		}

		public void PlayFor(int ms)
		{
			_bufClick.SetCurrentPosition(0);
			_bufClick.Play(0, BufferPlayFlags.Default);
//			Thread.Sleep(ms);
			PreciseDelay.Wait(ms);
			_bufClick.Stop();
			_bufClack.SetCurrentPosition(0);
			_bufClack.Play(0, BufferPlayFlags.Default);
		}

		public void Stop()
		{
			_bufClick.Stop();
			_bufClack.Stop();
		}
    }
}
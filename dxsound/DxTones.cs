//tabs=4
//-----------------------------------------------------------------------------
// TITLE:		DxTones.cs
//
// FACILITY:	DirectX Tone Generator
//
// ABSTRACT:	Generates radio tone sounds via Managed DirectX.
//
// ENVIRONMENT:	Microsoft.NET 2.0/3.5
//				Developed under Visual Studio.NET 2008
//
// AUTHOR:		Bob Denny, <rdenny@dc3.com>
//
// Edit Log:
//
// When			Who		What
//----------	---		-------------------------------------------------------
// 22-Apr-10	rbd		Play tones of arbitrary length
// 28-Apr-10	rbd		Remove sync parameter from Tone()
// 30-Apr-10	rbd		ITone interface
// 03-May-10	rbd		1.3.2 -  New PreciseDelay
// 07-May-10	rbd		1.5.0 - Refactor into separate assy, make class public.
//						Add Down() and Up().
// 11-May-10	rbd		1.5.0 - Volume Control!
// 18-May-10	rbd		1.5.0 - Volume 0 means absolutely silent.
// 19-May-10	rbd		1.5.0 - Stop before Down, prevent stuttering
// 03-Sep-10	rbd		1.6.0 - Generate tone on the fly for shaping, add LPF and
//						envelope shaping for clean tones.
// 03-Feb-11	rbd		1.7.0 - Soften tones more via additional envelope smoothing.
//						Increase the amplitude, was too weak requiring maxing out
//						of audio settings, especially for recording via "Stereo mix"
//						bus on Windows.
// 02-Jun-11	rbd		1.8.0 - Massive memory leaks in sound generation. Changes in
//						version 1.6.0 (above) moved things into procs so can get rid
//						of globals and dispose of resources properly. Use StartLatency
//						property for variable envelope shaping (aferthought + laziness).
// 27-Nov-11	rbd		1.9.0 - (SF #3432839) Manual key release tone now has 
//						shaped envelope. This turned out to be amazingly easy.
// 28-Nov-11	rbd		1.9.0 - (*SF #3432844) Add parameter for sound device 
//						selection to constructor. Implement IDisposable.
// 30-Nov-11	rbd		1.10.0 (SF #3446187) Change envelope to raised cosine. Also 
//						lengthen the total duration by half the rise/fall time so
//						that the tone is the proper duration at its half amplitude
//						level. 
// 11-Jun-12	rbd		3.0.0 - Rename StartLatency to RiseFallTime (finally)
//						
//
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.DirectX.DirectSound;
using System.Diagnostics;

namespace com.dc3.morse
{
    public class DxTones : ITone, IDisposable
    {
		private const int _sampleRate = 44100;
		private const short _bitsPerSample = 16;
		private const short _bytesPerSample = 2;

		private Device _deviceSound = null;											// [sentinel]
		private int _maxLen;														// Max length tone
		private float _freq;
		private double _filtCoeff;
		private float _volume;
		private int _rawVol;
		private int _ditMs;
		private int _riseFallTime;
		private int _totalSamples;
		private double _envelopeSamples;
		private double _cycleSamples;

		private BufferDescription _bufDesc = null;									// [sentinel]
		private SecondaryBuffer _secBuf = null;										// [sentinel]

		public DxTones(System.Windows.Forms.Control Handle, Guid DeviceGuid, int MaxLenMs)
        {
			_maxLen = MaxLenMs;
			this.Frequency = 880;													// Defaults (typ.)
			this.Volume = 1.0F;														// Max volume
			_ditMs = 80;
			_riseFallTime = 10;

			_deviceSound = new Microsoft.DirectX.DirectSound.Device(DeviceGuid);
			_deviceSound.SetCooperativeLevel(Handle, CooperativeLevel.Priority);	// Up priority for quick response
		}

		//
		// Generate the tone data
		//
		private void genWaveBuf(int duration)
		{
			try
			{
				byte[] waveBuf = GenTone(_freq, 0.9, duration);
				WaveFormat waveFmt = new WaveFormat();

				waveFmt.BitsPerSample = (short)_bitsPerSample;
				waveFmt.Channels = 1;
				waveFmt.BlockAlign = _bytesPerSample;

				waveFmt.FormatTag = WaveFormatTag.Pcm;
				waveFmt.SamplesPerSecond = _sampleRate;
				waveFmt.AverageBytesPerSecond = _sampleRate * _bytesPerSample;

				_bufDesc = new BufferDescription(waveFmt);
				_bufDesc.DeferLocation = true;
				_bufDesc.BufferBytes = waveBuf.Length;
				_bufDesc.ControlEffects = false;									// Necessary for short tones
				_bufDesc.GlobalFocus = true;										// Enable audio when program is in background
				_bufDesc.ControlVolume = true;
				if (_secBuf != null)												// If any previous buffer
					_secBuf.Dispose();												// Dispose of it now!
				_secBuf = new SecondaryBuffer(_bufDesc, _deviceSound);
				_secBuf.Write(0, waveBuf, LockFlag.EntireBuffer);
			}
			finally
			{
				if (_bufDesc != null)
					_bufDesc.Dispose();
			}


		}

		//
        // helper function for creating sound
        //
		private byte[] GenTone(double frequency, double amp, int duration)
        {
            int length = (int)(_sampleRate * ((double)duration + (_riseFallTime / 2.0)) / 1000.0);
			_totalSamples = length;
            byte[] wavedata = new byte[length * 2];
			double timeScale = frequency * 2 * Math.PI / (double)_sampleRate;

			_envelopeSamples = (double)_sampleRate * _riseFallTime / 1000.0 ;		// Cosine attack/decay from "StartLatency" (1.8)
			_cycleSamples = (double)_sampleRate / _freq;
			double xo = 0;
			double yo = 0;
            for (int i = 0; i < length; i++)
            {
				double a0;
				//double a0 = amp * Math.Min((double)i / _envelopeSamples, 1.0);		// Envelope
				//a0 = a0 * Math.Min((double)(length - i) / _envelopeSamples, 1.0);
				a0 = amp;
				if (i < _envelopeSamples)
					a0 *= Math.Min(0.5 - (0.5 * Math.Cos(Math.PI * i / _envelopeSamples)), 1.0);
				else if (i >= (length - _envelopeSamples))
					a0 *= Math.Min(0.5 - (0.5 * Math.Cos(Math.PI * (length - i) / _envelopeSamples)), 1.0);

				double xn = Math.Sin(i * timeScale);

				double yn = xn - (_filtCoeff * yo);									// Low pass filter
				xo = xn;
				yo = yn;
                short sh = (short)(yn * a0 * short.MaxValue);
                wavedata[i * 2] = (byte)(sh & 0x00FF); // low byte
                wavedata[i * 2 + 1] = (byte)(sh >> 8); // high byte
            }
            return wavedata;
        }

		//
		// Publics
		//
		public int MaxLenMs
		{
			get { return _maxLen; }
			set { _maxLen = value; }
		}

		public float Frequency
		{
			get { return _freq; }
			set { 
				_freq = value;
				_filtCoeff = Math.Exp((-Math.PI * _freq / (50.0 * _sampleRate)));	// Rolloff at freq / 50
			}
		}

		public float Volume
		{
			get { return _volume; }
			set
			{ 
				_volume = value;
				if (value == 0.0F)
					_rawVol = -9000;
				else
					_rawVol = -(int)Math.Pow((60 * (value - 1.0F)), 2);
			}
		}

		public int RiseFallTime
		{
			get { return _riseFallTime; }
			set { _riseFallTime = value; }
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
			PreciseDelay.Wait(_ditMs);
		}

		public void PlayFor(int ms)
		{
			genWaveBuf(ms);
			_secBuf.Volume = _rawVol;
			_secBuf.Play(0, BufferPlayFlags.Default);
			PreciseDelay.Wait(ms);
		}

		public void Stop()
		{
			if (_secBuf != null)
				_secBuf.Stop();
		}

		public void Down()
		{
			genWaveBuf(_maxLen);
			_secBuf.Stop();														// In case a dit or dah is playing
			_secBuf.Volume = _rawVol;
			_secBuf.Play(0, BufferPlayFlags.Default);
		}

		public void Up()
		{
			if (_secBuf != null)
			{
				//
				// This actually does work on faster machines, but leaves a discontinuity
				// in the waveform if the machine isn't fast enough.
				//
				int pos = _secBuf.PlayPosition / 2;								// Position in samples
				int sJump = (int)((((_maxLen - _riseFallTime) / 1000.0) - ((double)pos / _sampleRate)) * _freq);	// Whole cycles to start of delay
				pos += (int)((double)sJump * _cycleSamples);					// Jump forward n integral number of cycles!
		_secBuf.Stop();
				_secBuf.SetCurrentPosition(2 * pos);							// .. to start of decay envelope
		_secBuf.Play(0, BufferPlayFlags.Default);
				// Simply let the tail play now. For some reason, PreciseDelay does not delay in this situation, sometimes!
//				PreciseDelay.Wait(2 * _riseFallTime);							// Empirical, wait at least 20, but 2 * envelope
//				_secBuf.Stop();													// Just in case...
//				_secBuf.SetCurrentPosition(0);
			}
		}

		#region IDisposable Members

		public void Dispose()
		{
			if (_deviceSound != null)
				_deviceSound.Dispose();
			_deviceSound = null;
			if (_bufDesc != null)
				_bufDesc.Dispose();
			_bufDesc = null;
			if (_secBuf != null)
				_secBuf.Dispose();
			_secBuf = null;
		}

		#endregion
	}
}

#region usings
using System;
using System.ComponentModel.Composition;
using System.IO;

using NAudio;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VVVV.Audio;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

#endregion usings

namespace VVVV.Nodes
{
	public class FileStreamSignal : MultiChannelSignal
	{

		public bool FLoop;
		public bool FPlay = false;
		public bool FRunToEndBeforeLooping = true;
		public TimeSpan LoopStartTime;
		public TimeSpan LoopEndTime;
		public TimeSpan FSeekTime;
		
		public AudioFileReaderVVVV FAudioFile;
		
		public void OpenFile(string filename)
		{
			if (FAudioFile != null)
			{
				FAudioFile.Dispose();
				FAudioFile = null;
			}
			
			if(!string.IsNullOrEmpty(filename) && File.Exists(filename))
			{
				FAudioFile = new AudioFileReaderVVVV(filename, 44100);
				SetOutputCount(FAudioFile.WaveFormat.Channels);
			}
			else
			{
				SetOutputCount(0);
			}
		}
		
		float[] FFileBuffer = new float[1];
		protected override void FillBuffers(float[][] buffer, int offset, int sampleCount)
		{
			var channels = FAudioFile.WaveFormat.Channels;
			var samplesToRead = sampleCount*channels;
			FFileBuffer = BufferHelpers.Ensure(FFileBuffer, samplesToRead);
            int bytesread = 0;
			if(FPlay)
			{
	            bytesread = FAudioFile.Read(FFileBuffer, offset*channels, samplesToRead);
	
	            if (bytesread == 0)
	            {
	            	if(FLoop)
	            	{
	            		FAudioFile.CurrentTime = LoopStartTime;
	            		FRunToEndBeforeLooping = false;
		                bytesread = FAudioFile.Read(FFileBuffer, offset*channels, samplesToRead);  		
	            	}
	            	else
	            	{
	            		bytesread = FFileBuffer.ReadSilence(offset*channels, samplesToRead);
	            	}
					
	            }
	            else
	            {
	            	if(FLoop && FAudioFile.CurrentTime >= LoopEndTime)
	            	{
	            		FAudioFile.CurrentTime = LoopStartTime;
	            		FRunToEndBeforeLooping = false;
		                //bytesread = FAudioFile.Read(FFileBuffer, offset*channels, samplesToRead);  		
	            	}
	            }
	            
	            //copy to output buffers
				for (int i = 0; i < channels; i++)
				{
					for (int j = 0; j < sampleCount; j++)
					{
						buffer[i][j] = FFileBuffer[i + j*channels];
					}
				}	            
			}
			else //silence
			{
				for (int i = 0; i < channels; i++) 
				{
					buffer[i].ReadSilence(offset, sampleCount);
				}
			}
						
		}

		public override void Dispose()
		{
			FAudioFile.Dispose();
			base.Dispose();
		}		
	}
	
	
	#region PluginInfo
	[PluginInfo(Name = "FileStream", Category = "VAudio", Help = "Plays Back sound files", Tags = "wav, mp3, aiff", Author = "beyon")]
	#endregion PluginInfo
	public class FileStreamNode : GenericMultiAudioSourceNodeWithOutputs<FileStreamSignal>
	{
		#region fields & pins
		[Input("Play")]
		public IDiffSpread<bool> FPlay;
		
		[Input("Loop")]
		public IDiffSpread<bool> FLoop;
		
		[Input("Loop Start Time")]
		public IDiffSpread<double> FLoopStart;
		
		[Input("Loop End Time")]
		public IDiffSpread<double> FLoopEnd;
		
		[Input("Do Seek", IsBang = true)]
		public IDiffSpread<bool> FDoSeek;
		
		[Input("Seek Time")]
		public IDiffSpread<double> FSeekPosition;		
		
		[Input("Volume", DefaultValue = 1f)]
		public IDiffSpread<float> FVolume;
		
		[Input("Filename", StringType = StringType.Filename, FileMask="Audio File (*.wav, *.mp3, *.aiff, *.m4a)|*.wav;*.mp3;*.aiff;*.m4a")]
		public IDiffSpread<string> FFilename;
		
		[Output("Duration")]
		public ISpread<double> FDurationOut;
		
		[Output("Position")]
		public ISpread<double> FPositionOut;
			
		[Output("Can Seek")]
		public ISpread<bool> FCanSeekOut;
		
		[Output("Sample Rate")]
        public ISpread<int> FSampleRateOut;

		[Output("Channels")]
        public ISpread<int> FChannelsOut;
		
        [Output("Uncompressed Format")]
        public ISpread<string> FFileFormatOut;
		#endregion fields & pins
		

		protected override void SetParameters(int i, FileStreamSignal instance)
		{
			if(FFilename.IsChanged)
			{
				instance.OpenFile(FFilename[i]);
                
                if (instance.FAudioFile == null)
                {
                    FDurationOut[i] = 0;
                    FCanSeekOut[i] = false;
                    FSampleRateOut[i] = 0;
                    FChannelsOut[i] = 0;
                    FFileFormatOut[i] = "";
                }
                else
                {
                	instance.FAudioFile.Volume = FVolume[i];
                	instance.FLoop = FLoop[i];
                	instance.LoopStartTime = TimeSpan.FromSeconds(FLoopStart[i]);
                	instance.LoopEndTime = TimeSpan.FromSeconds(FLoopEnd[i]);
                	
                	SetOutputSliceCount(CalculatedSpreadMax);
                	
                    FDurationOut[i] = instance.FAudioFile.TotalTime.TotalSeconds;
                    FCanSeekOut[i] = instance.FAudioFile.CanSeek;
                    FChannelsOut[i] = instance.FAudioFile.OriginalFileFormat.Channels;
                    FSampleRateOut[i] = instance.FAudioFile.OriginalFileFormat.SampleRate;
                    FFileFormatOut[i] = instance.FAudioFile.OriginalFileFormat.ToString();
                }
		
			}
			
			if (instance.FAudioFile == null) return;

            if (FVolume.IsChanged)
            {
            	instance.FAudioFile.Volume = FVolume[i];
            }
            
            if(FPlay.IsChanged)
            {
            	instance.FPlay = FPlay[i];
            }
            
			if(FLoop.IsChanged)
			{
				if(FLoop[i] && instance.FAudioFile.CurrentTime <= instance.LoopEndTime)
				{
					instance.FRunToEndBeforeLooping = false;
				}
				else if (FLoop[i])
				{
					instance.FRunToEndBeforeLooping = true;
				}
				instance.FLoop = FLoop[i];
			}
			
			if(FLoopStart.IsChanged)
			{
				instance.LoopStartTime = TimeSpan.FromSeconds(FLoopStart[i]);
			}	
			
			if(FLoopEnd.IsChanged)
			{
				instance.LoopEndTime = TimeSpan.FromSeconds(Math.Min(FLoopEnd[i], instance.FAudioFile.TotalTime.TotalSeconds));
			}
			
			//TODO: write sample based looping
			if(FLoop[i] && !instance.FRunToEndBeforeLooping)
			{
				if(instance.FAudioFile.CurrentTime > instance.LoopEndTime)
				{
					instance.FAudioFile.CurrentTime = instance.LoopStartTime;
				}
			}
			
			if(FDoSeek[i] && instance.FAudioFile.CanSeek)
			{
				if(instance.FSeekTime > instance.LoopEndTime) instance.FRunToEndBeforeLooping = true;
				if(instance.FSeekTime < instance.LoopStartTime) instance.FRunToEndBeforeLooping = false;
				instance.FAudioFile.CurrentTime = TimeSpan.FromSeconds(FSeekPosition[i]);
			}
		}
		
		protected override void SetOutputSliceCount(int sliceCount)
		{
			FPositionOut.SliceCount = sliceCount;
			FDurationOut.SliceCount = sliceCount;
			FChannelsOut.SliceCount = sliceCount;
			FSampleRateOut.SliceCount = sliceCount;
			FCanSeekOut.SliceCount = sliceCount;
			FFileFormatOut.SliceCount = sliceCount;
		}
		
		protected override void SetOutputs(int i, FileStreamSignal instance)
		{
			if(instance.FAudioFile == null)
			{
				FPositionOut[i] = 0;
			}
			else
			{
				FPositionOut[i] = instance.FAudioFile.CurrentTime.TotalSeconds;
			}
		}

        protected override FileStreamSignal GetInstance(int i)
		{
			return new FileStreamSignal();
		}
	}
	
}

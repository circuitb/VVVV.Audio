﻿#region usings
using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using VVVV.Audio;

using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;
using NAudio.Utils;


using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
	
	
	public abstract class AudioSignalOperator : AudioSignal
	{
		public AudioSignalOperator()
		{
		}
		
		private ISpread<AudioSignal> FInputs;
		public ISpread<AudioSignal> Inputs
		{
			get
			{
				return FInputs;
			}
			set
			{
				FInputs = value;
			}
		}
		
		protected abstract void Operation(float[] accumulator, float[] operant, int offset, int count);
		
		private float[] FTempBuffer = new float[1];
		protected override void FillBuffer(float[] buffer, int offset, int count)
		{
			FTempBuffer = BufferHelpers.Ensure(FTempBuffer, count);

			if(FInputs != null && FInputs.SliceCount > 0)
			{
				bool first = true;
				for(int slice = 0; slice < FInputs.SliceCount; slice++)
				{
					if(FInputs[slice] != null)
					{
						if(first)
						{
							FInputs[slice].Read(buffer, offset, count);
							first = false;
						}
						else //rest
						{
							FInputs[slice].Read(FTempBuffer, offset, count);
							Operation(buffer, FTempBuffer, offset, count);
						}
					}
				}
			}
		}
	}
	
	public class AudioSignalMultiplyOperator : AudioSignalOperator
	{
		protected override void Operation(float[] accumulator, float[] operant, int offset, int count)
		{
			for (int i = offset; i < count; i++)
			{
				accumulator[i] *= operant[i];
			}
		} 
	}
	
	public class AudioSignalAddOperator : AudioSignalOperator
	{
		protected override void Operation(float[] accumulator, float[] operant, int offset, int count)
		{
			for (int i = offset; i < count; i++)
			{
				accumulator[i] += operant[i];
			}
		}
	}
	
	public class AudioSignalSubtractOperator : AudioSignalOperator
	{
		protected override void Operation(float[] accumulator, float[] operant, int offset, int count)
		{
			for (int i = offset; i < count; i++)
			{
				accumulator[i] -= operant[i];
			}
		}
	}
	
	public class SignalOperationNode<TOperator> : IPluginEvaluate where TOperator : AudioSignalOperator, new()
	{
		[Input("Input", IsPinGroup = true)]
		IDiffSpread<ISpread<AudioSignal>> Inputs;
		
		[Output("Audio Out")]
		ISpread<AudioSignal> OutBuffer;

		public void Evaluate(int SpreadMax)
		{
			if(Inputs.IsChanged)
			{
				//old signals must be disposed
				OutBuffer.Resize(0, () => { return new AudioSignal(); }, s => { if(s != null) s.Dispose(); });
				for(int outSlice=0; outSlice<SpreadMax; outSlice++)
				{
					var sig = new TOperator();
					sig.Inputs = new Spread<AudioSignal>(Inputs.SliceCount);
					
					for(int i=0; i<Inputs.SliceCount; i++)
					{
						sig.Inputs[i] = Inputs[i][outSlice];
					}
					
					OutBuffer.Add(sig);
				}
			}
		}
	}
	
	public class SignalOperationSpectralNode<TOperator> : IPluginEvaluate where TOperator : AudioSignalOperator, new()
	{
		[Input("Input")]
		IDiffSpread<ISpread<AudioSignal>> Inputs;
		
		[Output("Audio Out")]
		ISpread<AudioSignal> OutBuffer;

		public void Evaluate(int SpreadMax)
		{
			if(Inputs.IsChanged)
			{
				OutBuffer.Resize(Inputs.SliceCount, () => { return new TOperator(); }, s => { if(s != null) s.Dispose(); });
				for(int outSlice=0; outSlice<OutBuffer.SliceCount; outSlice++)
				{
					if(OutBuffer[outSlice] == null) OutBuffer[outSlice] = new TOperator();
					(OutBuffer[outSlice] as AudioSignalOperator).Inputs = Inputs[outSlice];
				}
			}
		}
	}
	
	[PluginInfo(Name = "Multiply", Category = "Audio", Help = "Multiplies audio signals", AutoEvaluate = true, Tags = "AM")]
	public class SignalMultiplyNode: SignalOperationNode<AudioSignalMultiplyOperator>
	{
	}
	
	[PluginInfo(Name = "Multiply", Category = "Audio", Version = "Spectral", Help = "Multiplies audio signals", AutoEvaluate = true, Tags = "AM")]
	public class SignalMultiplySpectralNode: SignalOperationSpectralNode<AudioSignalMultiplyOperator>
	{
	}
	
	[PluginInfo(Name = "Add", Category = "Audio", Help = "Adds audio signals", AutoEvaluate = true, Tags = "Mix")]
	public class SignalAddNode: SignalOperationNode<AudioSignalAddOperator>
	{
	}
	
	[PluginInfo(Name = "Add", Category = "Audio", Version = "Spectral", Help = "Adds audio signals", AutoEvaluate = true, Tags = "Mix")]
	public class SignalAddSpectralNode: SignalOperationSpectralNode<AudioSignalAddOperator>
	{
	}
	
	[PluginInfo(Name = "Subtract", Category = "Audio", Help = "Subtracts audio signals", AutoEvaluate = true, Tags = "Difference")]
	public class SignalSubtractNode: SignalOperationNode<AudioSignalSubtractOperator>
	{
	}
	
	[PluginInfo(Name = "Subtract", Category = "Audio", Version = "Spectral", Help = "Subtracts audio signals", AutoEvaluate = true, Tags = "Difference")]
	public class SignalSubtractSpectralNode: SignalOperationSpectralNode<AudioSignalSubtractOperator>
	{
	}
	

}



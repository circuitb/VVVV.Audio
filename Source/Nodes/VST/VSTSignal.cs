﻿#region usings
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

using Jacobi.Vst.Core;
using Jacobi.Vst.Framework;
using Jacobi.Vst.Interop.Host;
using NAudio.CoreAudioApi;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.Wave.SampleProviders;
using VVVV.Audio;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.PluginInterfaces.V2.Graph;
using VVVV.PluginInterfaces.V2.NonGeneric;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using System.IO;

#endregion usings

namespace VVVV.Audio.VST
{
	public class VSTSignal : MultiChannelInputSignal
	{
		public VstPluginContext PluginContext;
        internal PluginInfoForm InfoForm;
		protected int FInputCount, FOutputCount;
		IWin32Window FOwnerWindow;
		
		public VSTSignal(string filename, IWin32Window ownerWindow)
		{
			FOwnerWindow = ownerWindow;
            Filename = filename;
		}

        private string FFilename;

        public string Filename
        {
            get { return FFilename; }
            set 
            {
                if (value != FFilename)
                {
                    FDoProcess = false;
                    FFilename = value;
                    Load(FFilename);
                }
            }
        }


        public string[] ProgramNames = new string[0];

        protected void Load(string filename)
        {
            if (File.Exists(filename))
            {

                PluginContext = OpenPlugin(filename);
                if(PluginContext == null) return;

                SetOutputCount(PluginContext.PluginInfo.AudioOutputCount);

                PluginContext.PluginCommandStub.MainsChanged(true);
                PluginContext.PluginCommandStub.SetSampleRate(WaveFormat.SampleRate);
                PluginContext.PluginCommandStub.SetBlockSize(AudioService.Engine.Settings.BufferSize);

                PluginContext.PluginCommandStub.StartProcess();

                FInputCount = PluginContext.PluginInfo.AudioInputCount;
                FOutputCount = PluginContext.PluginInfo.AudioOutputCount;

                FInputMgr = new VstAudioBufferManager(FInputCount, AudioService.Engine.Settings.BufferSize);
                FOutputMgr = new VstAudioBufferManager(FOutputCount, AudioService.Engine.Settings.BufferSize);

                FInputBuffers = FInputMgr.ToArray();
                FOutputBuffers = FOutputMgr.ToArray();

                // plugin does not support processing audio
                if ((PluginContext.PluginInfo.Flags & VstPluginFlags.CanReplacing) == 0)
                {
                    MessageBox.Show("This plugin does not process any audio.");
                    return;
                }

                FCanEvents = PluginContext.PluginCommandStub.CanDo("receiveVstMidiEvent") == VstCanDoResult.Yes; 

                InfoForm = new PluginInfoForm();
                InfoForm.PluginContext = PluginContext;
                InfoForm.DataToForm();
                InfoForm.Dock = DockStyle.Fill;

                GetPluginInfo();
                GetProgramNames();

                if (PluginChanged != null)
                    PluginChanged();

                FDoProcess = true;
            }
        }

        //gets called when the plugin was changed
        public Action PluginChanged
        {
            get;
            set;
        }
        
		void GetPluginInfo()
		{
//			PluginContext.PluginInfo.PluginID;
//			
//			 ListViewItem lvItem = new ListViewItem(PluginContext.PluginCommandStub.GetEffectName());
//                lvItem.SubItems.Add(PluginContext.PluginCommandStub.GetProductString());
//                lvItem.SubItems.Add(PluginContext.PluginCommandStub.GetVendorString());
//                lvItem.SubItems.Add(PluginContext.PluginCommandStub.GetVendorVersion().ToString());
//                lvItem.SubItems.Add(PluginContext.Find<string>("PluginPath"));
		}

        
        private void GetProgramNames()
        {
        	ProgramNames = new string[PluginContext.PluginInfo.ProgramCount];
        	
        	for (int i = 0; i < ProgramNames.Length; i++)
            {
                ProgramNames[i] = PluginContext.PluginCommandStub.GetProgramNameIndexed(i);
            }
        	
        	//HACK: very evil hack
//            var ctx = OpenPlugin(FFilename);
//
//            for (int i = 0; i < ctx.PluginInfo.ProgramCount; i++)
//            {
//                ctx.PluginCommandStub.SetProgram(i);
//                ProgramNames[i] = ctx.PluginCommandStub.GetProgramName();
//            }
//            
//            ctx.Dispose();
        }

        private void SetNeedsSafe(int index)
        {
            NeedsSave = true;
            ParamIndex = index;
            if (LastParamChangeInfo != null)
            {
                string name = PluginContext.PluginCommandStub.GetParameterName(index);
                string label = PluginContext.PluginCommandStub.GetParameterLabel(index);
                string display = PluginContext.PluginCommandStub.GetParameterDisplay(index);

                LastParamChangeInfo(name + " " + label + " " + display);
            }
        }

        public Action<string> LastParamChangeInfo
        {
            get;
            set;
        }

        public int ParamIndex
        {
            get;
            protected set;
        }

        #region Save plugin state
        public string GetSaveString()
        {
            byte[] chunk = null;
            if(PluginContext.PluginInfo.Flags.HasFlag(VstPluginFlags.ProgramChunks))
            {
                chunk = PluginContext.PluginCommandStub.GetChunk(true);
            }
            else //serialize the floats
            {
                var count = PluginContext.PluginInfo.ParameterCount;

                chunk = new byte[count * 4];

                for (int i = 0; i < count; i++)
                {
                    var floatBytes = BitConverter.GetBytes(PluginContext.PluginCommandStub.GetParameter(i));

                    for (int j = 0; j < 4; j++)
                    {
                        chunk[i * 4 + j] = floatBytes[j];
                    }
                }
            }

            

            if (chunk != null)
                return Convert.ToBase64String(chunk);// + "|" + PluginContext.PluginInfo.PluginID.ToString();
            else
                return "";

        }

        public void LoadFromSafeString(string safeString)
        {
            //safeString.LastIndexOf('|');

            if (string.IsNullOrWhiteSpace(safeString) || PluginContext == null) return;

            if (PluginContext.PluginInfo.Flags.HasFlag(VstPluginFlags.ProgramChunks))
            {
                PluginContext.PluginCommandStub.BeginSetProgram();
                PluginContext.PluginCommandStub.SetChunk(Convert.FromBase64String(safeString), true);
                PluginContext.PluginCommandStub.EndSetProgram();
            }
            else
            {

                var count = PluginContext.PluginInfo.ParameterCount;

                var data = Convert.FromBase64String(safeString);

                if(count == data.Length/4)
                {
                    for (int i = 0; i < count; i++)
                    {
                        PluginContext.PluginCommandStub.SetParameter(i, BitConverter.ToSingle(data, i*4));
                    }
                }
            }
        }
        #endregion

        private void HostCmdStub_PluginCalled(object sender, PluginCalledEventArgs e)
		{
			HostCommandStub hostCmdStub = (HostCommandStub)sender;
			
			// can be null when called from inside the plugin main entry point.
			if (hostCmdStub.PluginContext.PluginInfo != null)
			{
				Debug.WriteLine("Plugin " + hostCmdStub.PluginContext.PluginInfo.PluginID + " called:" + e.Message);
			}
			else
			{
				Debug.WriteLine("The loading Plugin called:" + e.Message);
			}
		}
		
        //load from file
		private VstPluginContext OpenPlugin(string pluginPath)
		{
			try
			{
				HostCommandStub hostCmdStub = new HostCommandStub();
				hostCmdStub.PluginCalled += new EventHandler<PluginCalledEventArgs>(HostCmdStub_PluginCalled);
                hostCmdStub.RaiseSave = SetNeedsSafe;
				
				VstPluginContext ctx = VstPluginContext.Create(pluginPath, hostCmdStub);
				var midiOutChannels = ctx.PluginCommandStub.GetNumberOfMidiOutputChannels();
				
				//if(midiOutChannels > 0)
				{
					hostCmdStub.FProcessEventsAction = ReceiveEvents;
				}
				
				// add custom data to the context
				ctx.Set("PluginPath", pluginPath);
				ctx.Set("HostCmdStub", hostCmdStub);
				
				// actually open the plugin itself
				ctx.PluginCommandStub.Open();
				
				return ctx;
			}
			catch (Exception e)
			{
				MessageBox.Show(FOwnerWindow, e.ToString(), "VST Load", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			
			return null;
		}
		
		public VstEventCollection MidiOutEvents;
		
		void ReceiveEvents(VstEvent[] events)
		{
			if(events.Length > 0)
			{
				if(MidiOutEvents == null)
					MidiOutEvents = new VstEventCollection(events);
				else
					MidiOutEvents.AddRange(events);
			}
		}

        //format changes
        protected override void Engine_SampleRateChanged(object sender, EventArgs e)
        {
            base.Engine_SampleRateChanged(sender, e);
            if (PluginContext != null)
            {
                PluginContext.PluginCommandStub.StopProcess();
                PluginContext.PluginCommandStub.SetSampleRate(WaveFormat.SampleRate);
                PluginContext.PluginCommandStub.StartProcess();
            }
        }

        protected override void Engine_BufferSizeChanged(object sender, EventArgs e)
        {
            base.Engine_BufferSizeChanged(sender, e);
            if (PluginContext != null)
            {
                PluginContext.PluginCommandStub.StopProcess();
                PluginContext.PluginCommandStub.SetBlockSize(BufferSize);
                PluginContext.PluginCommandStub.StartProcess();
            }
        }

        //midi events
        public VstEventCollection MidiEvents = new VstEventCollection();
        private bool FCanEvents;
        private bool FHasEvents;
        public void SetMidiEvent(byte msg, byte data1, byte data2)
        {
            if (FCanEvents)
            {
                VstEvent evt = new VstMidiEvent(0, 0, 0, new byte[] { msg, data1, data2 }, 0, 0);
                MidiEvents.Add(evt);
                FHasEvents = true;
            }
        }
		
        //unmanaged buffers
		VstAudioBufferManager FInputMgr = new VstAudioBufferManager(2, 1);
        VstAudioBufferManager FOutputMgr = new VstAudioBufferManager(2, 1);
		VstAudioBuffer[] FInputBuffers;
		VstAudioBuffer[] FOutputBuffers;
        
		protected void ManageBuffers(int count)
		{
			if(FInputMgr.BufferSize != count)
			{
				FInputMgr.Dispose();
				FOutputMgr.Dispose();
				
				FInputMgr = new VstAudioBufferManager(FInputCount, count);
				FOutputMgr = new VstAudioBufferManager(FOutputCount, count);
				
				FInputBuffers = FInputMgr.ToArray();
				FOutputBuffers = FOutputMgr.ToArray();
			}
		}
		
        //process
        bool FDoProcess;
		protected override void FillBuffers(float[][] buffer, int offset, int count)
		{
            if (PluginContext != null && FDoProcess)
            {
                ManageBuffers(count);

                if (FInput != null)
                {
                    FInputMgr.ClearAllBuffers();
                    for (int b = 0; b < FInputCount; b++)
                    {
                        var inSig = FInput[b];
                        if (inSig != null)
                        {
                            var vstBuffer = FInputBuffers[b % FInputCount];

                            //read input, use buffer[0] as temp buffer
                            inSig.Read(buffer[0], offset, count);

                            //copy to vst buffer
                            for (int i = 0; i < count; i++)
                            {
                                vstBuffer[i] += buffer[0][i];
                            }
                        }
                    }
                }

                //add events?
                if (FHasEvents)
                {
                    PluginContext.PluginCommandStub.ProcessEvents(MidiEvents.ToArray());
                    MidiEvents.Clear();
                    FHasEvents = false;
                }
                //process the shit
                PluginContext.PluginCommandStub.ProcessReplacing(FInputBuffers, FOutputBuffers);


                for (int i = 0; i < FOutputBuffers.Length; i++)
                {
                    for (int j = 0; j < count; j++)
                    {
                        buffer[i][j] = FOutputBuffers[i][j];
                    }
                }
            }
		}
		
		public override void Dispose()
		{
            FDoProcess = false;

			//close and dispose vst
            if (PluginContext != null && PluginContext.PluginCommandStub != null)
			{
				PluginContext.PluginCommandStub.StopProcess();
				PluginContext.PluginCommandStub.MainsChanged(false);
				PluginContext.Dispose();
                
			}

            if (FInputMgr != null)
            {
                FInputMgr.Dispose();
                FOutputMgr.Dispose();
            }
			
			base.Dispose();
		}


        public bool NeedsSave { get; set; }
    }
}



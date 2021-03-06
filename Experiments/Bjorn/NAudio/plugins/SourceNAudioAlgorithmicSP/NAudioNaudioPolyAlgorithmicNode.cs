#region usings
using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using NAudio.Wave;
using System.CodeDom.Compiler;
using System.Text;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo

	#endregion PluginInfo
	public class NAudioPolyAlgorithmicNode : IDisposable, IPluginEvaluate
	{
		#region fields & pins

		[Input("A", DefaultValue = 1.0)]
		IDiffSpread<double> FA;

		[Input("B", DefaultValue = 1.0)]
		IDiffSpread<double> FB;

		[Input("C", DefaultValue = 1.0)]
		IDiffSpread<double> FC;

		[Input("D", DefaultValue = 1.0)]
		IDiffSpread<double> FD;

		[Input("Algorithm", DefaultString = "t")]
		IDiffSpread<string> FAlgorithm;

		[Input("Volume", DefaultValue = 0.25)]
		IDiffSpread<double> FVolume;

		[Input("Pan", DefaultValue = 0.0)]
		IDiffSpread<double> FPan;

		[Output("Output")]
		ISpread<WaveChannel32> FOutput;

		[Output("Status", DefaultString = "Default")]
		ISpread<string> FStatus;

		[Import()]
		ILogger FLogger;
		#endregion fields & pins

		private WaveChannel32 chan = null;
		private AlgoStream algo = null;

		public NAudioPolyAlgorithmicNode()
		{
			algo = new AlgoStream();
			chan = new WaveChannel32(algo, 0.25f, 0f);
		}

		public void Dispose()
		{
			if (chan != null) {
				chan.Dispose();
				chan = null;
			}
			if (algo != null) {
				algo.Dispose();
				algo = null;
			}

		}
		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{

			if (FVolume.IsChanged) {
				chan.Volume = (float)FVolume[0];
			}
			if (FPan.IsChanged) {
				chan.Pan = (float)FPan[0];
			}
			/*if(FA.IsChanged || FB.IsChanged || FC.IsChanged || FD.IsChanged)
			{
				algo.SetVariables(FA[0], FB[0], FC[0], FD[0]);
			}*/
			if (FAlgorithm.IsChanged) {
				FStatus[0] = algo.SetAlgorithm(FAlgorithm[0]);
			}

			if (algo.bufferCount < (algo.BufferedA.Length - 1)) {
				algo.BufferedA[algo.bufferCount] = FA[0];
				algo.BufferedB[algo.bufferCount] = FB[0];
				algo.BufferedC[algo.bufferCount] = FC[0];
				algo.BufferedD[algo.bufferCount] = FD[0];
				algo.bufferCount += 1;
			}


			FOutput[0] = chan;
			//FLogger.Log(LogType.Debug, "hi tty!");
		}


		public class AlgoStream : WaveStream
		{
			private long t;
			private Type _generatorType;
			public double[] BufferedA;
			public double[] BufferedB;
			public double[] BufferedC;
			public double[] BufferedD;
			public int bufferCount;

			public AlgoStream()
			{
				this.t = 0;
				this.BufferedA = new double[320];
				this.BufferedB = new double[320];
				this.BufferedC = new double[320];
				this.BufferedD = new double[320];
				this.bufferCount = 0;
			}

			public override long Position { get; set; }
			public override long Length {
				get { return long.MaxValue; }
			}
			public override WaveFormat WaveFormat {
				get {
					//!!!! change to float format
					WaveFormat wf = new WaveFormat(8000, 8, 1);
					return wf;
				}
			}

			public override int Read(byte[] buffer, int offset, int count)
			{
				int samples = count;
				int index = 0;

				double accumulator = 0.0;

				//double prevA = 0.0;

				double increment = 0.0;

				if (samples > 0) {
					increment = bufferCount / samples;
				}

				for (int i = 0; i < samples; i++) {
					index = (int)accumulator;

					if (_generatorType != null) {

						buffer[i] = (byte)_generatorType.GetMethod("Compute").Invoke(null, new object[] {
							t,
							BufferedA[index],
							BufferedB[index],
							BufferedC[index],
							VMath.Lerp(BufferedD[index], BufferedD[index + 1], accumulator - index)
						});

						accumulator += increment;
						t++;
					} else {
						buffer[i] = 127;
					}
				}

				bufferCount = 0;

				return count;
			}

			//Copy&Pasted from: http://www.david-gouveia.com/one-line-algorithmic-music-in-xna/
			//and then adapted to this example
/*public void SetVariables(double A, double B, double C, double D)
			{
				a = A;
				b = B;
				c = C;
				d = D;
			}*/
						public string SetAlgorithm(string algorithm)
			{
				// Ignore if the user wrote nothing
				if (String.IsNullOrEmpty(algorithm)) {
					_generatorType = null;
					return "Empty";

				} else {
					// Create code string
					StringBuilder codeBuilder = new StringBuilder();
					codeBuilder.AppendLine("using System;");
					codeBuilder.AppendLine("namespace VVVV.Nodes {");
					codeBuilder.AppendLine("    public static class ComputeAlgo {");
					codeBuilder.AppendLine("    \tprivate static double Sin(long a) { return Math.Sin(a); }");
					codeBuilder.AppendLine("    \tprivate static double Sin(double a) { return Math.Sin(a); }");
					//codeBuilder.AppendLine("    	private static double Tone(double t, double freq) { return Sin(t/((8000/freq)/(Math.PI*2))); }");
					codeBuilder.AppendLine("    \tprivate static double Tone(double t, double freq) { return Sin((t * 2 * Math.PI * freq)/8000); }");
					codeBuilder.AppendLine("    \tprivate static double fmOp(long time, double volume, double pitch)\r\n\t        \t\t\t\t\t\t\t\t\t\t{ return volume*Math.Sin(((double)time)/((8000/pitch)/(Math.PI*2))); }");
					codeBuilder.AppendLine("        public static byte Compute(long t, double A, double B, double C, double D) { return (byte)(" + algorithm + "); }");
					codeBuilder.AppendLine("    }");
					codeBuilder.AppendLine("}");
					string code = codeBuilder.ToString();

					// Compile code string in memory
					CodeDomProvider codeDomProvider = CodeDomProvider.CreateProvider("C#");
					CompilerParameters compileParams = new CompilerParameters {
						GenerateExecutable = false,
						GenerateInMemory = true
					};
					CompilerResults compilerResults = codeDomProvider.CompileAssemblyFromSource(compileParams, new[] { code });

					// On error display message and exit
					if (compilerResults.Errors.HasErrors) {
						_generatorType = null;
						return GetErrorMessage(compilerResults);
					} else {
						// Otherwise store a Type reference to the assembly we compiled
						// Using reflection we can call the Generate method from this Type

						_generatorType = compilerResults.CompiledAssembly.GetType("VVVV.Nodes.ComputeAlgo");
						return "Ok";
					}
				}
			}

			private static string GetErrorMessage(CompilerResults compilerResults)
			{
				StringBuilder errorBuilder = new StringBuilder();
				foreach (CompilerError compilerError in compilerResults.Errors)
					errorBuilder.AppendLine(compilerError.ErrorText);
				return errorBuilder.ToString();
			}
		}


	}
}

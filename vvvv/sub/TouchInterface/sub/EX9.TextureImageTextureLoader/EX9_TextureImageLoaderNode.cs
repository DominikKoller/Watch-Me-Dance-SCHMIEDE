#region usings
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using SlimDX;
using SlimDX.Direct3D9;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.PluginInterfaces.V2.EX9;
using VVVV.Utils.SlimDX;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using VVVV.Utils;

#endregion usings

namespace VVVV.Nodes
{
	//little helper class used to store information for each
	//texture resource
	public class FrameData
	{
		public int Width;
		public int Height;
		public byte[] Pixels;
		public bool Copied;
	}
	
	public enum GifDisposal : byte
	{
	    None = 0,
	    DoNotDispose = 1,
	    RestoreBackgroundColor = 2,
	    RestorePrevious = 3
	}
	
	#region PluginInfo
	[PluginInfo(Name = "ImageLoader", Category = "EX9.Texture", Help = "Loads image files into textures using the Windows.Media API", Tags = "bmp, gif, icon, png, tiff, wmp")]
	#endregion PluginInfo
	public class EX9_TextureImageLoader : IPluginEvaluate, IPartImportsSatisfiedNotification
	{
		[Input("Filename", DefaultValue = 1, StringType = StringType.Filename)]
		public IDiffSpread<string> FFilenameIn;
		
		[Input("Reload", IsBang = true)]
		public IDiffSpread<bool> FReloadIn;
		
		[Output("Texture Out", BinName = "Frame Count")]
		public ISpread<ISpread<TextureResource<FrameData>>> FTextureOut;
		
		[Output("Delay (ms)")]
		public ISpread<ISpread<int>> FDelayOut;
		
		[Output("Position")]
		public ISpread<ISpread<Vector2D>> FPositionOut;
		
		[Output("Size")]
		public ISpread<ISpread<Vector2D>> FSizeOut;
		
		[Output("Disposal")]
		public ISpread<ISpread<GifDisposal>> FDisposalOut;
		
		[Output("Original Format")]
		public ISpread<string> FFormatOut;
		
		[Output("Loading")]
		public ISpread<bool> FLoadingOut;
		
		[Output("Progress")]
		public ISpread<double> FProgressOut;
		
		[Import()]
		public ILogger FLogger;
		private Spread<ImageLoaderSlice> FGifDecoders = new Spread<ImageLoaderSlice>(0);
		
		public void OnImportsSatisfied()
		{
			//spreads have a length of one by default, change it
			//to zero so ResizeAndDispose works properly.
			FTextureOut.SliceCount = 0;
		}
		
		//called when data for any output pin is requested
		public void Evaluate(int spreadMax)
		{
			//resize and create
			FGifDecoders.ResizeAndDismiss(spreadMax, () => { return new ImageLoaderSlice(FLogger); });
			
			if(FFilenameIn.IsChanged || FReloadIn.IsChanged)
			{
				FTextureOut.SliceCount = spreadMax;
				FLoadingOut.SliceCount = spreadMax;
				FFormatOut.SliceCount = spreadMax;
				FProgressOut.SliceCount = spreadMax;
				FDelayOut.SliceCount = spreadMax;
				FPositionOut.SliceCount = spreadMax;
				FSizeOut.SliceCount = spreadMax;
				FDisposalOut.SliceCount = spreadMax;
				
				for (int i = 0; i < spreadMax; i++)
				{
					var decoder = FGifDecoders[i];
					
//					//set params
//					var task = new Task(() => decoder.OpenFileAsync(FFilenameIn[i], FReloadIn[i]));
//                    task.Start();
                    decoder.OpenFileAsync(FFilenameIn[i], FReloadIn[i]);
				}
			}
			
			//set loading
			for (int i = 0; i < spreadMax; i++)
			{
				var decoder = FGifDecoders[i];
				
				FLoadingOut[i] = decoder.IsLoading;
				FProgressOut[i] = decoder.Progress;
				
				if(decoder.HasNewData)
				{
					FLogger.Log(LogType.Debug, "New Data: " + i);
				    //set outputs
					FTextureOut[i] = decoder.TextureSpread;
					FFormatOut[i] = decoder.Format.ToString();
					FDelayOut[i] = decoder.DelaySpread;
					FPositionOut[i] = decoder.PositionSpread;
					FSizeOut[i] = decoder.SizeSpread;
					FDisposalOut[i] = decoder.DisposalSpread;
					decoder.HasNewData = false;
				}
			}
			
		}
	}
	
	public class ImageLoaderSlice
	{	
		
		//properties
		private Spread<TextureResource<FrameData>> FTextureSpread = new Spread<TextureResource<FrameData>>(0);
		
		public ISpread<TextureResource<FrameData>> TextureSpread
		{
			get
			{
				return FTextureSpread;
			}
		}
		
		public ISpread<int> DelaySpread
		{
		    get;
		    private set;
		}
		
		public ISpread<Vector2D> PositionSpread
		{
		    get;
		    private set;
		}
		
		public ISpread<Vector2D> SizeSpread
		{
		    get;
		    private set;
		}
		
		public ISpread<GifDisposal> DisposalSpread
		{
		    get;
		    private set;
		}
		
		public PixelFormat Format
		{
			get;
			private set;
		}
		
		public bool IsLoading
		{
			get;
			private set;
		}
		
		public bool HasNewData
		{
			get;
			set;
		}
		
		public double Progress
		{
			get;
			private set;
		}

		//constructor
		bool FFirstLoad;
		static ILogger FLogger;
		public ImageLoaderSlice(ILogger logger)
		{
			FLogger = logger;
			FFirstLoad = true;
		}

		List<FrameData> FFrameDataList;
		string FLastFilename = "";
		public void OpenFileAsync(string filename, bool forceReload = false)
		{
			//valid file name?
			if(string.IsNullOrEmpty(filename) || (FLastFilename == filename && !forceReload)) return;
			
			IsLoading = true;
			
			//dispose all textures
			FTextureSpread.ResizeAndDispose(0, i => CreateTextureResource(i));
			
			FLastFilename = filename;
			
			//start async loading
			FLogger.Log(LogType.Debug, "Task: " + filename);
			//Task.Run( () => CreateFrameData(filename, forceReload));
			//Task.Factory.StartNew(() => CreateFrameData(filename, forceReload));
			CreateFrameData(filename, forceReload);
			FLogger.Log(LogType.Debug, "Finished: " + filename);
			
		}

		//async loading task
		private void CreateFrameData(string filename, bool forceReload = false)
		{
			Progress = 0;
			var creationOptions = (forceReload || FFirstLoad) ? BitmapCreateOptions.IgnoreImageCache : BitmapCreateOptions.None;
			FFirstLoad = false;
			
			//create decoder
			BitmapDecoder decoder;
			try
			{
				var client = new System.Net.WebClient();
				byte[] imageData = client.DownloadData(filename);
				var img = Image.FromStream(new MemoryStream(imageData));
				decoder = BitmapDecoder.Create(new Uri(filename, UriKind.RelativeOrAbsolute), creationOptions, BitmapCacheOption.Default);
			}
			catch (Exception e)
			{
				FLogger.Log(LogType.Error, e.Message);
				return;
			}

			if(decoder.IsDownloading)
			{	
				FLogger.Log(LogType.Debug, "download");
				decoder.DownloadCompleted += decoder_DownloadCompleted;
				decoder.DownloadProgress += decoder_DownloadProgress;
			}
			else
			{
				FLogger.Log(LogType.Debug, "direct");
			    FillFrameList(decoder);
			}
		}
		
		
		void decoder_DownloadCompleted(object sender, System.EventArgs e)
		{
			FLogger.Log(LogType.Debug, "Download Completed");
		    FillFrameList(sender as BitmapDecoder);
		}
		
		void FillFrameList(BitmapDecoder decoder)
		{
		    //continue if loaded
		    Progress = 1;
		    var list = new List<FrameData>(decoder.Frames.Count);
		    var delaySpread = new Spread<int>(0);
		    var positionSpread = new Spread<Vector2D>(0);
		    var sizeSpread = new Spread<Vector2D>(0);
		    var disposalSpread = new Spread<GifDisposal>(0);
		    
		    Format = decoder.Frames[0].Format;
		    
		    //convert and copy frames
		    foreach (var frame in decoder.Frames)
		    {
		        
		        //read meta data
		        if(!frame.IsFrozen)
		            frame.Freeze();
		        
		        var meta = frame.Metadata as BitmapMetadata;
		        if(meta != null)
		        {
		            if(meta.ContainsQuery("/grctlext/Delay"))
		                delaySpread.Add((ushort)meta.GetQuery("/grctlext/Delay"));
		            else
		                delaySpread.Add(0);
		            
		            if(meta.ContainsQuery("/grctlext/Disposal"))
		                disposalSpread.Add((GifDisposal)((byte)meta.GetQuery("/grctlext/Disposal")));
		            else
		                disposalSpread.Add(GifDisposal.None);
		            
		            var left = (ushort)0;
		            var top = (ushort)0;
		            
		             if(meta.ContainsQuery("/imgdesc/Left"))
		                 left = (ushort)meta.GetQuery("/imgdesc/Left");
		             
		             if(meta.ContainsQuery("/imgdesc/Top"))
		                 top = (ushort)meta.GetQuery("/imgdesc/Top");
		            
		            positionSpread.Add(new Vector2D(left, top));
		        }
		        
		        sizeSpread.Add(new Vector2D(frame.PixelWidth, frame.PixelHeight));
		        
		        var convertedBitmap = new FormatConvertedBitmap();
		        
		        convertedBitmap.BeginInit();
		        convertedBitmap.Source = frame;
		        convertedBitmap.DestinationFormat = PixelFormats.Bgra32;
		        convertedBitmap.EndInit();

		        list.Add(CopyBitmapFrameToFrameData(convertedBitmap));
		    }
		    
		    FFrameDataList = list;
		    
		    //recreate texture resources
		    FTextureSpread.ResizeAndDispose(FFrameDataList.Count, i => CreateTextureResource(i));
		    DelaySpread = delaySpread;
		    PositionSpread = positionSpread;
		    SizeSpread = sizeSpread;
		    DisposalSpread = disposalSpread;
		    IsLoading = false;
		    HasNewData = true;
		}
		
		//set download progress
		void decoder_DownloadProgress(object sender, DownloadProgressEventArgs e)
		{
		    Progress = e.Progress/100.0;
		}
		
		//create textures
		TextureResource<FrameData> CreateTextureResource(int slice)
		{
		    var res = TextureResource.Create(FFrameDataList[slice], CreateTexture, UpdateTexture);
		    res.NeedsUpdate = false;
		    return res;
		}
		
		//this method gets called, when Reinitialize() was called in evaluate,
		//or a graphics device asks for its data
		Texture CreateTexture(FrameData info, Device device)
		{
			info.Copied = false;
			return TextureUtils.CreateTexture(device, Math.Max(info.Width, 1), Math.Max(info.Height, 1));
		}
		
		
		//this method gets called, when Update() was called in evaluate,
		//or a graphics device asks for its texture, here you fill the texture with the actual data
		//this is called for each renderer, careful here with multiscreen setups, in that case
		//calculate the pixels in evaluate and just copy the data to the device texture here
		unsafe void UpdateTexture(FrameData info, Texture texture)
		{
			if(!info.Copied)
			{
			    CopyByteArrayToTexture(info.Pixels, new Rectangle(0, 0, info.Width, info.Height), texture);
				info.Copied = true;
			}
			
		}
		
		//copy helpers
		private FrameData CopyBitmapFrameToFrameData(BitmapSource bm)
		{

			int stride = bm.PixelWidth * (bm.Format.BitsPerPixel / 8);
			var bytes = new byte[stride * bm.PixelHeight];
			try
			{
				bm.CopyPixels(bytes, stride, 0);
			}
			catch(Exception e)
			{
				throw e;
			}
			
			return new FrameData{ Width = bm.PixelWidth, Height = bm.PixelHeight, Pixels = bytes };
		}
		
		public static void CopyByteArrayToTexture(byte[] bm, Rectangle area, Texture texture)
		{
			var rect = texture.LockRectangle(0, area, LockFlags.None);
			
			try
			{
				if (rect.Data.Length == bm.Length) 
				{
					Marshal.Copy(bm, 0, rect.Data.DataPointer, (int)rect.Data.Length);
				}
				else 
				{
					for (int i = 0; i < area.Height; i++) //line by line
					{
						Marshal.Copy(bm, area.Width * i * 4, rect.Data.DataPointer.Move(rect.Pitch * i), area.Width * 4);
					}
				}
			}
			catch(Exception e)
			{
				throw e;
			}
			finally
			{
				texture.UnlockRectangle(0);
			}
			
		}
		
	}
}

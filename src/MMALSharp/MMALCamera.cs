﻿using MMALSharp.Components;
using MMALSharp.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MMALSharp.Handlers;
using MMALSharp.Ports;

namespace MMALSharp
{    
    /// <summary>
    /// This class provides an interface to the Raspberry Pi camera module. 
    /// </summary>
    public sealed class MMALCamera
    {
        /// <summary>
        /// Reference to the camera component
        /// </summary>
        public MMALCameraComponent Camera { get; set; }

        /// <summary>
        /// List of all encoders currently in the pipeline
        /// </summary>
        public List<MMALDownstreamComponent> DownstreamComponents { get; set; }

        /// <summary>
        /// List of all established connections
        /// </summary>
        public List<MMALConnectionImpl> Connections { get; set; }

        /// <summary>
        /// Reference to the Video splitter component which attaches to the Camera's video output port
        /// </summary>
        public MMALSplitterComponent Splitter { get; set; }

        private static readonly Lazy<MMALCamera> lazy = new Lazy<MMALCamera>(() => new MMALCamera());

        public static MMALCamera Instance => lazy.Value;

        private MMALCamera()
        {
            BcmHost.bcm_host_init();

            this.Camera = new MMALCameraComponent();
            this.DownstreamComponents = new List<MMALDownstreamComponent>();
            this.Connections = new List<MMALConnectionImpl>();
        }

        /// <summary>
        /// Begin capture on one of the camera's output ports.
        /// </summary>
        /// <param name="port">An output port of the camera component</param>
        public void StartCapture(MMALPortImpl port)
        {            
            if (port == this.Camera.StillPort || port == this.Camera.VideoPort)
            {
                port.SetImageCapture(true);
            }                
        }

        /// <summary>
        /// Stop capture on one of the camera's output ports
        /// </summary>
        /// <param name="port">An output port of the camera component</param>
        public void StopCapture(MMALPortImpl port)
        {
            if (port == this.Camera.StillPort || port == this.Camera.VideoPort)
            {
                port.SetImageCapture(false);
            }                
        }
        
        /// <summary>
        /// Force capture to stop on a port (Still or Video)
        /// </summary>
        /// <param name="port">The capture port</param>
        public void ForceStop(MMALPortImpl port)
        {
            if(port.Trigger.CurrentCount > 0)
            {
                port.Trigger.Signal();
            }            
        }

        /// <summary>
        /// Record video for a specified amount of time. 
        /// </summary>        
        /// <param name="connPort">Port the encoder is connected to</param>                
        /// <param name="timeout">A timeout to stop the video capture</param>
        /// <param name="split">Used for Segmented video mode</param>
        /// <returns>The awaitable Task</returns>
        public async Task TakeVideo(MMALPortImpl connPort, DateTime? timeout = null, Split split = null)
        {
            var connection = this.Connections.Where(c => c.OutputPort == connPort).FirstOrDefault();

            if (connection == null)
            {
                throw new PiCameraError("No connection currently established on the port specified");
            }

            var encoder = connection.DownstreamComponent as MMALVideoEncoder;

            if (encoder == null)
            {
                throw new PiCameraError("No video encoder currently attached to output port specified");
            }
                                    
            if(split != null && !MMALCameraConfig.InlineHeaders)
            {
                Helpers.PrintWarning("Inline headers not enabled. Split mode not supported when this is disabled.");
                split = null;
            }

            this.CheckPreviewComponentStatus();
            
            try
            {
                Console.WriteLine($"Preparing to take video. Resolution: {encoder.Width} x {encoder.Height}. " +
                                  $"Encoder: {encoder.OutputPort.EncodingType.EncodingName}. Pixel Format: {encoder.OutputPort.PixelFormat.EncodingName}.");
                                
                ((MMALVideoPort)encoder.Outputs.ElementAt(0)).Timeout = timeout;
                ((MMALVideoEncoder)encoder).Split = split;

                await BeginProcessing(encoder, this.Camera.VideoPort);
                
            }
            finally
            {
                encoder.Handler.PostProcess();
            }            
        }

        /// <summary>
        /// Capture raw image data directly from the Camera component - this method does not use an Image encoder.
        /// </summary>
        /// <returns>The awaitable Task</returns>
        public async Task TakeRawPicture(ICaptureHandler handler)
        {
            var connection = this.Connections.Where(c => c.OutputPort == this.Camera.StillPort).FirstOrDefault();
            
            if (connection != null)
            {
                throw new PiCameraError("A connection was found to the Camera still port. No encoder should be connected to the Camera's still port for raw capture.");
            }

            if (handler == null)
            {
                throw new PiCameraError("No handler specified");
            }

            this.Camera.Handler = handler;

            this.CheckPreviewComponentStatus();

            //Enable the image encoder output port.            
            try
            {
                Console.WriteLine($"Preparing to take raw picture - Resolution: {MMALCameraConfig.StillResolution.Width} x {MMALCameraConfig.StillResolution.Height}. " +
                                  $"Encoder: {MMALCameraConfig.StillEncoding.EncodingName}. Pixel Format: {MMALCameraConfig.StillSubFormat.EncodingName}.");

                await BeginProcessing(this.Camera, this.Camera.StillPort, MMALCameraComponent.MMALCameraStillPort);
            }
            finally
            {
                this.Camera.Handler.PostProcess();
                this.Camera.Handler.Dispose();
            }
        }

        /// <summary>
        /// Captures a single image from the output port specified. Expects an MMALImageEncoder to be attached.
        /// </summary>                
        /// <param name="connPort">The port our encoder is attached to</param>       
        /// <param name="rawBayer">Include raw bayer metadeta in the capture</param>        
        /// <param name="useExif">Specify whether to include EXIF tags in the capture</param>
        /// <param name="exifTags">Custom EXIF tags to use in the capture</param>
        /// <returns>The awaitable Task</returns>
        public async Task TakePicture(MMALPortImpl connPort, bool rawBayer = false, bool useExif = true, params ExifTag[] exifTags)
        {
            var connection = this.Connections.Where(c => c.OutputPort == connPort).FirstOrDefault();

            if (connection == null)
            {
                throw new PiCameraError("No connection currently established on the port specified");
            }

            //Find the encoder/decoder which is connected to the output port specified.
            var encoder = connection.DownstreamComponent as MMALImageEncoder;
            
            if (encoder == null)
            {
                throw new PiCameraError("No image encoder currently attached to output port specified");
            }
            
            if (useExif)
            {
                ((MMALImageEncoder)encoder).AddExifTags(exifTags);
            }

            this.CheckPreviewComponentStatus();

            if (rawBayer)
            {
                this.Camera.StillPort.SetRawCapture(true);
            }
                
            if (MMALCameraConfig.EnableAnnotate)
            {
                ((MMALImageEncoder)encoder).AnnotateImage();
            }
            
            //Enable the image encoder output port.            
            try
            {
                Console.WriteLine($"Preparing to take picture. Resolution: {encoder.Width} x {encoder.Height}. " +
                                  $"Encoder: {encoder.OutputPort.EncodingType.EncodingName}. Pixel Format: {encoder.OutputPort.PixelFormat.EncodingName}.");

                await BeginProcessing(encoder, this.Camera.StillPort);
            }
            finally
            {
                encoder.Handler.PostProcess();
            }            
        }

        /// <summary>
        /// Takes images until the moment specified in the timeout parameter has been met.
        /// </summary>
        /// <param name="connPort">The port our encoder is attached to</param>
        /// <param name="timeout">Take images until this timeout is hit</param>       
        /// <param name="rawBayer">Include raw bayer metadeta in the capture</param>        
        /// <param name="useExif">Specify whether to include EXIF tags in the capture</param>
        /// <param name="exifTags">Custom EXIF tags to use in the capture</param>
        /// <returns>The awaitable Task</returns>
        public async Task TakePictureTimeout(MMALPortImpl connPort, DateTime timeout, bool rawBayer = false, bool useExif = true, bool burstMode = false, params ExifTag[] exifTags)
        {    
            if(burstMode)
            {
                this.Camera.StillPort.SetParameter(MMALParametersCamera.MMAL_PARAMETER_CAMERA_BURST_CAPTURE, true);
            }

            while (DateTime.Now.CompareTo(timeout) < 0)
            {                             
                await TakePicture(connPort, rawBayer, useExif, exifTags);                
            }
        }

        /// <summary>
        /// Takes a timelapse image. You can specify the interval between each image taken and also when the operation should finish.
        /// </summary>
        /// <param name="connPort">The port our encoder is attached to</param>
        /// <param name="tl">Specifies settings for the Timelapse</param>       
        /// <param name="rawBayer">Include raw bayer metadeta in the capture</param>        
        /// <param name="useExif">Specify whether to include EXIF tags in the capture</param>
        /// <param name="exifTags">Custom EXIF tags to use in the capture</param>
        /// <returns>The awaitable Task</returns>
        public async Task TakePictureTimelapse(MMALPortImpl connPort, Timelapse tl, bool rawBayer = false, bool useExif = true, params ExifTag[] exifTags)
        {           
            int interval = 0;

            if(tl == null)
            {
                throw new PiCameraError("Timelapse object null. This must be initialized for Timelapse mode");
            }
            
            while (DateTime.Now.CompareTo(tl.Timeout) < 0)
            {
                switch (tl.Mode)
                {
                    case TimelapseMode.Millisecond:
                        interval = tl.Value;
                        break;
                    case TimelapseMode.Second:
                        interval = tl.Value * 1000;
                        break;
                    case TimelapseMode.Minute:
                        interval = (tl.Value * 60) * 1000;
                        break;
                }

                await Task.Delay(interval);
                
                await TakePicture(connPort, rawBayer, useExif, exifTags);                            
            }
        }

        /// <summary>
        /// Helper method to begin processing image data. Starts the Camera port and awaits until processing is complete.
        /// Cleans up resources upon finish.
        /// </summary>
        /// <param name="component">The component we are processing data on</param>
        /// <param name="cameraPort">The camera port which image data is coming from</param>
        /// <param name="outputPort">The output port we are processing data from</param>
        /// <returns>The awaitable Task</returns>
        public async Task BeginProcessing(MMALComponentBase component, MMALPortImpl cameraPort, int outputPort = 0)
        {
            //Enable all connections associated with this component
            component.EnableConnections();

            component.Start(outputPort, new Action<MMALBufferImpl, MMALPortBase>(component.ManagedOutputCallback));

            this.StartCapture(cameraPort);

            //Wait until the process is complete.
            component.Outputs.ElementAt(outputPort).Trigger = new Nito.AsyncEx.AsyncCountdownEvent(1);
            await component.Outputs.ElementAt(outputPort).Trigger.WaitAsync();

            this.StopCapture(cameraPort);

            //Disable the image encoder output port.
            component.Stop(outputPort);

            //Close open connections.
            component.DisableConnections();
            
            component.CleanPortPools();
        }
        
        /// <summary>
        /// Disables processing on the camera component
        /// </summary>
        public void DisableCamera()
        {
            this.DownstreamComponents.ForEach(c => c.DisableComponent());
            this.Camera.DisableComponent();
        }

        /// <summary>
        /// Enables processing on the camera component
        /// </summary>
        public void EnableCamera()
        {
            this.DownstreamComponents.ForEach(c => c.EnableComponent());
            this.Camera.EnableComponent();
        }

        /// <summary>
        /// Reconfigures the Camera's still port. This should be called when you change the Still port resolution or encoding/pixel format types.
        /// </summary>
        /// <returns>The camera instance</returns>
        public MMALCamera ReconfigureStill()
        {
            this.DisableCamera();

            this.Connections.Where(c => c.OutputPort == this.Camera.StillPort).ToList()?.ForEach(c => c.Disable());

            this.Camera.InitialiseStill();

            this.Connections.Where(c => c.OutputPort == this.Camera.StillPort).ToList()?.ForEach(c => c.Enable());

            this.EnableCamera();

            return this;
        }

        /// <summary>
        /// Reconfigures the Camera's video port. This should be called when you change the Video port resolution or encoding/pixel format types.
        /// </summary>
        /// <returns>The camera instance</returns>
        public MMALCamera ReconfigureVideo()
        {
            this.DisableCamera();

            this.Connections.Where(c => c.OutputPort == this.Camera.VideoPort).ToList()?.ForEach(c => c.Disable());

            this.Camera.InitialiseVideo();

            this.Connections.Where(c => c.OutputPort == this.Camera.VideoPort).ToList()?.ForEach(c => c.Enable());

            this.EnableCamera();

            return this;
        }

        /// <summary>
        /// Reconfigures the Camera's preview port. This should be called when you change the Video port resolution 
        /// </summary>
        /// <returns>The camera instance</returns>
        public MMALCamera ReconfigurePreview()
        {
            this.DisableCamera();

            this.Connections.Where(c => c.OutputPort == this.Camera.PreviewPort).ToList()?.ForEach(c => c.Disable());
         
            this.Camera.InitialisePreview();
          
            this.Connections.Where(c => c.OutputPort == this.Camera.PreviewPort).ToList()?.ForEach(c => c.Enable());

            this.EnableCamera();

            return this;
        }

        /// <summary>
        /// This applies the configuration settings against the camera such as Saturation, Contrast etc.
        /// </summary>
        /// <returns>The camera instance</returns>
        public MMALCamera ConfigureCameraSettings()
        {
            Debugger.Print("Configuring camera parameters.");

            this.DisableCamera();
            this.Camera.SetCameraParameters();            
            this.EnableCamera();

            return this;
        }
       
        /// <summary>
        /// Helper method to check the Renderer component status. If a Renderer has not been initialized, a warning will
        /// be shown to the user. If a Renderer has been created but a connection has not been initialized, this will be 
        /// done automatically for the user.
        /// </summary>
        private void CheckPreviewComponentStatus()
        {
            //Create connections
            if (!this.Connections.Any(c => c.OutputPort == this.Camera.PreviewPort))
            {
                Helpers.PrintWarning("Preview port does not have a Render component configured. Resulting image will be affected.");
            }
        }

        /// <summary>
        /// Cleans up any unmanaged resources. It is intended for this method to be run when no more activity is to be done on the camera.
        /// </summary>
        public void Cleanup()
        {
            Debugger.Print("Destroying final components");
            
            var tempList = new List<MMALDownstreamComponent>(this.DownstreamComponents);

            tempList.ForEach(c => c.Dispose());
            this.Camera.Dispose();

            BcmHost.bcm_host_deinit();
        }
                 
    }
    
}

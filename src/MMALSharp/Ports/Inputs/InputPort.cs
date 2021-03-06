// <copyright file="InputPort.cs" company="Techyian">
// Copyright (c) Ian Auty. All rights reserved.
// Licensed under the MIT License. Please see LICENSE.txt for License info.
// </copyright>

using System;
using System.Runtime.InteropServices;
using MMALSharp.Callbacks;
using MMALSharp.Callbacks.Providers;
using MMALSharp.Common.Utility;
using MMALSharp.Handlers;
using MMALSharp.Native;
using MMALSharp.Ports.Outputs;
using static MMALSharp.MMALNativeExceptionHelper;

namespace MMALSharp.Ports.Inputs
{
    /// <summary>
    /// Represents an input port.
    /// </summary>
    public unsafe class InputPort : InputPortBase
    {
        private int _width;
        private int _height;

        /// <inheritdoc />
        public override Resolution Resolution
        {
            get
            {
                if (_width == 0 || _height == 0)
                {
                    return new Resolution(this.Ptr->Format->Es->Video.Width, this.Ptr->Format->Es->Video.Height);
                }

                return new Resolution(_width, _height);
            }

            internal set
            {
                _width = value.Pad().Width;
                _height = value.Pad().Height;

                this.Ptr->Format->Es->Video.Width = value.Pad().Width;
                this.Ptr->Format->Es->Video.Height = value.Pad().Height;
            }
        }

        /// <inheritdoc />
        internal override IInputCallbackHandler ManagedInputCallback { get; set; }
        
        /// <summary>
        /// Monitor lock for input port callback method.
        /// </summary>
        internal static object InputLock = new object();

        /// <summary>
        /// Creates a new instance of <see cref="InputPort"/>. 
        /// </summary>
        /// <param name="ptr">The native pointer.</param>
        /// <param name="comp">The component this port is associated with.</param>
        /// <param name="type">The type of port.</param>
        /// <param name="guid">Managed unique identifier for this component.</param>
        public unsafe InputPort(MMAL_PORT_T* ptr, MMALComponentBase comp, PortType type, Guid guid) 
            : base(ptr, comp, type, guid)
        {
        }
        
        /// <summary>
        /// Creates a new instance of <see cref="InputPort"/>. 
        /// </summary>
        /// <param name="ptr">The native pointer.</param>
        /// <param name="comp">The component this port is associated with.</param>
        /// <param name="type">The type of port.</param>
        /// <param name="guid">Managed unique identifier for this component.</param>
        /// <param name="handler">The capture handler.</param>
        public unsafe InputPort(MMAL_PORT_T* ptr, MMALComponentBase comp, PortType type, Guid guid, ICaptureHandler handler) 
            : base(ptr, comp, type, guid, handler)
        {
        }
        
        /// <inheritdoc />
        internal override unsafe void EnableInputPort()
        {
            if (!this.Enabled)
            {
                this.ManagedInputCallback = InputCallbackProvider.FindCallback(this);

                this.NativeCallback = new MMALPort.MMAL_PORT_BH_CB_T(this.NativeInputPortCallback);

                IntPtr ptrCallback = Marshal.GetFunctionPointerForDelegate(this.NativeCallback);

                MMALLog.Logger.Debug("Enabling input port.");

                if (this.ManagedInputCallback == null)
                {
                    MMALLog.Logger.Warn("Callback null");

                    MMALCheck(MMALPort.mmal_port_enable(this.Ptr, IntPtr.Zero), "Unable to enable port.");
                }
                else
                {
                    MMALCheck(MMALPort.mmal_port_enable(this.Ptr, ptrCallback), "Unable to enable port.");
                }

                this.InitialiseBufferPool();
            }

            if (!this.Enabled)
            {
                throw new PiCameraError("Unknown error occurred whilst enabling port");
            }
        }

        /// <inheritdoc />
        internal override void ReleaseInputBuffer(MMALBufferImpl bufferImpl)
        {            
            bufferImpl.Release();
            
            if (this.Enabled && this.BufferPool != null)
            {
                MMALBufferImpl newBuffer;
                while (true)
                {
                    newBuffer = this.BufferPool.Queue.GetBuffer();
                    if (newBuffer != null)
                    {
                        break;
                    }
                }
                
                // Populate the new input buffer with user provided image data.
                var result = this.ManagedInputCallback.Callback(newBuffer);
                newBuffer.ReadIntoBuffer(result.BufferFeed, result.DataLength, result.EOF);

                try
                {
                    if (!this.Trigger && result.EOF)
                    {
                        MMALLog.Logger.Debug("Received EOF. Releasing.");
                        
                        newBuffer.Release();                        
                        newBuffer = null;
                        this.Trigger = true;
                    }

                    if (newBuffer != null)
                    {
                        this.SendBuffer(newBuffer);
                    }
                    else
                    {
                        MMALLog.Logger.Warn("Buffer null. Continuing.");
                    }
                }
                catch (Exception ex)
                {
                    MMALLog.Logger.Warn($"Buffer handling failed. {ex.Message}");
                    throw;
                }
            }
        }

        /// <inheritdoc />
        internal override void Start()
        {
            this.EnableInputPort();
        }

        internal virtual unsafe void NativeInputPortCallback(MMAL_PORT_T* port, MMAL_BUFFER_HEADER_T* buffer)
        {
            lock (InputLock)
            {
                if (MMALCameraConfig.Debug)
                {
                    MMALLog.Logger.Debug("In native input callback");
                }

                var bufferImpl = new MMALBufferImpl(buffer);

                bufferImpl.PrintProperties();

                this.ReleaseInputBuffer(bufferImpl);
            }
        }
    }
}
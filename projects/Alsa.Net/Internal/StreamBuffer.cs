
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Alsa.Net.Internal
{
    /// <summary>
    /// Push-pull buffer. This stream accumulates bytes from a source and transfers them to a consumer. Thread-safe.
    /// </summary>
    public sealed class StreamBuffer : Stream, IDisposable
    {
        private readonly ReaderWriterLockSlim sync = new ReaderWriterLockSlim();
        private readonly List<byte> buffer = new List<byte>();
        private readonly ManualResetEventSlim reset = new ManualResetEventSlim(false);
        private bool isDisposed;
        private bool ended;
        private int bytesPushed;
        private int bytesPulled;

        /// <summary>
        /// Push-pull buffer. This stream accumulates bytes from a source and transfers them to a consumer. Thread-safe.
        /// </summary>
        public StreamBuffer()
        {
        }

        /// <summary>
        /// Always true.
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// Always false.
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// Always false.
        /// </summary>
        public override bool CanWrite => false;

        /// <summary>
        /// Always -1.
        /// </summary>
        public override long Length
        {
            get
            {
                ////this.sync.EnterReadLock();
                try
                {
                    return -1;
                }
                finally
                {
                    ////this.sync.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Returns the number of bytes pulled.
        /// </summary>
        public override long Position
        {
            get
            {
                this.sync.EnterReadLock();
                try
                {
                    return this.bytesPulled;
                }
                finally
                {
                    this.sync.ExitReadLock();
                }
            }
            set { throw new NotSupportedException("Position cannot be set."); }
        }
        
        public bool HasEnded
        {
            get
            {
                lock (this.buffer)
                {
                    return this.buffer.Count == 0 && this.ended;
                }
            }
        }

        public void Append(byte[] buffer, int offset, int count)
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(this.ToString());

            this.sync.EnterWriteLock();
            try
            {
                if (offset == 0 && count == buffer.Length)
                {
                    this.buffer.AddRange(buffer);
                    this.bytesPushed += buffer.Length;
                }
                else
                {
                    var partial = new byte[count - offset];
                    Array.Copy(buffer, offset, partial, 0, partial.Length);
                    this.buffer.AddRange(partial);
                    this.bytesPushed += partial.Length;
                }

                this.reset.Set();
            }
            finally
            {
                this.sync.ExitWriteLock();
            }
        }

        public void SetEnd()
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(this.ToString());

            this.sync.EnterWriteLock();
            try
            {
                this.ended = true;
                this.reset.Set();
            }
            finally
            {
                this.sync.ExitWriteLock();
            }
        }

        public int Pull(byte[] buffer, int offset, int count)
        {
            if (this.isDisposed)
                throw new ObjectDisposedException(this.ToString());

            this.sync.EnterWriteLock();
            try
            {
                // Is there enough byte to pull ? If not pull everything
                var length = count - offset; // how many bytes are desired?
                length = length < this.buffer.Count ? length : this.buffer.Count; // how many bytes are available?

                // extract and fill
                for (int i = 0; i < length; i++)
                {
                    buffer[offset + i] = this.buffer[i];
                }

                this.buffer.RemoveRange(0, length);

                this.bytesPulled += length;
                return length;
            }
            finally
            {
                this.sync.ExitWriteLock();
            }
        }

        public int WaitPull(byte[] buffer, int offset, int count)
        {
            int length;
            bool ended;
            this.sync.EnterReadLock();
            try
            {
                // Is there enough bytes to pull ? 
                length = count - offset; // how many bytes are desired?
                length = length < this.buffer.Count ? length : this.buffer.Count; // how many bytes are available?
                ended = this.ended;
            }
            finally
            {
                this.sync.ExitReadLock();
            }

            if (length == 0 && !ended)
            {
                // no data available, wait for more
                this.reset.Wait();
            }
            else if (length == 0 && ended)
            {
                return 0;
            }

            this.sync.EnterWriteLock();
            try
            {
                if (!this.ended)
                {
                    // reset event for next call
                    this.reset.Reset();
                }

                length = count - offset; // how many bytes are desired?
                length = length < this.buffer.Count ? length : this.buffer.Count; // how many bytes are available?

                // extract and fill
                for (int i = 0; i < length; i++)
                {
                    buffer[offset + i] = this.buffer[i];
                }

                this.buffer.RemoveRange(0, length);

                this.bytesPulled += length;
                return length;
            }
            finally
            {
                this.sync.ExitWriteLock();
            }
        }

        public override void Flush()
        {
            this.reset.Set();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.WaitPull(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.Append(buffer, offset, count);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // this is called by HttpClient
            var read = this.WaitPull(buffer, offset, buffer.Length);
            return read;
        }

        public override int ReadByte()
        {
            throw new NotSupportedException();
        }

        public override int Read(Span<byte> buffer)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            // this is called by HttpClient
            var array = new byte[buffer.Length];
            var read = this.WaitPull(array, 0, buffer.Length);
            ////Array.Copy(array, 0, buffer.Span, 0, read);
            for (int i = 0; i < read; i++)
            {
                buffer.Span[i] = array[i];
            }

            return read;
        }

        public override string ToString()
        {
            return base.ToString() + " In:" + this.bytesPushed + " Pending:" + this.buffer.Count + " Out:" + this.bytesPulled;
        }

        protected override void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    this.buffer.Clear();
                    this.sync.Dispose();
                    this.reset.Dispose();
                }

                this.isDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}

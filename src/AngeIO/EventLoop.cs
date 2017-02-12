#region License
/*
 * The MIT License
 *
 * Copyright Li Jia
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AngeIO {
    /// <summary>
    /// Process events and delayed events on a single thread.
    /// </summary>
    public class EventLoop : SynchronizationContext {
        struct Event {
            public long time;
            public SendOrPostCallback callback;
            public object state;

            public Event(long time, SendOrPostCallback callback, object state) {
                this.time = time;
                this.callback = callback;
                this.state = state;
            }
        }

        int _eventCount;
        int _eventStart;
        Event[] _events;
        AutoResetEvent _wakeEvent;

        int _timerCount;
        Event[] _timers;

        long _maximumFrameTicks;
        long _totalTicks;
        long _prevTicks;

        TaskScheduler _taskScheduler;

        public double CurrentTime {
            get {
                return (double)_totalTicks / TimeSpan.TicksPerSecond;
            }
        }

        public static new EventLoop Current {
            get {
                return SynchronizationContext.Current as EventLoop;
            }
        }

        public TaskScheduler TaskScheduler {
            get { return _taskScheduler; }
        }

        public long TotalTicks {
            get {
                return _totalTicks;
            }

            set {
                _totalTicks = value;
            }
        }

        public EventLoop(long maximumTicks = 0) {
            _events = new Event[32];
            _timers = new Event[32];
            _wakeEvent = new AutoResetEvent(false);
            _maximumFrameTicks = maximumTicks;
            _taskScheduler = new EventLoopTaskScheduler(this);
        }

        public override void Send(SendOrPostCallback d, object state) {
            // Runs on current thread
            if (Current == this) {
                d(state);
            }

            // Post and wait
            else {
                var finishedEvent = new AutoResetEvent(false);
                Exception innerException = null;
                Post((s) => {
                    try {
                        d(state);
                    }
                    catch (Exception e) {
                        innerException = e;
                    }
                    finally {
                        finishedEvent.Set();
                    }
                }, null);
                finishedEvent.WaitOne();
                finishedEvent.Dispose();
                if (innerException != null) {
                    throw innerException;
                }
            }
        }

        public override void Post(SendOrPostCallback d, object state) {
            lock (this) {
                AddEvent(new Event(_totalTicks, d, state));
                _wakeEvent.Set();
            }
        }

        public void PostDelay(double delay, SendOrPostCallback d, object state) {
            long time = _totalTicks + (long)(delay * TimeSpan.TicksPerSecond);
            lock (this) {
                ++_timerCount;
                int active = _timerCount + HEAP0 - 1;

                if (_timers.Length < active + 1) {
                    int newLength = (active + 1) + ((active + 1) / 2);
                    var newTimers = new Event[newLength];
                    Array.Copy(_timers, newTimers, active);
                    _timers = newTimers;
                }

                _timers[active] = new Event(time, d, state);
                upheap(_timers, active);
                _wakeEvent.Set();
            }
        }

        #region Extra post and send methods.

        public void Post(Action action) {
            Post(ActionCallback, action);
        }

        public void Post<T>(Action<T> action, T state) {
            Post((s) => action(state), null);
        }

        public void Post(Task task) {
            Post(TaskCallback, task);
        }

        public void PostDelay(double delay, Action action) {
            PostDelay(delay, ActionCallback, action);
        }

        public void PostDelay(double delay, Task task) {
            PostDelay(delay, TaskCallback, task);
        }

        public void PostDelay<T>(double delay, Action<T> action, T state) {
            PostDelay(delay, (s) => action(state), null);
        }

        static void ActionCallback(object state) {
            ((Action)state)();
        }

        static void TaskCallback(object task) {
            ((Task)task).RunSynchronously();
        }

        #endregion

        public void Run() {
            SetSynchronizationContext(this);
            _prevTicks = DateTime.Now.Ticks;
            _totalTicks = 0;

            while (true) {
                UpdateTimers();

                var e = new Event();
                lock (this) {
                    if (_eventCount > 0) {
                        e = _events[_eventStart];
                        _eventStart = (_eventStart + 1) % _events.Length;
                        _eventCount--;
                    }
                }

                if (e.callback != null) {
                    e.callback(e.state);
                    continue;
                }
                else {
                    var hastimer = false;
                    var timeout = new TimeSpan();
                    lock(this) {
                        hastimer = _timerCount > 0;
                        if (hastimer) timeout = new TimeSpan(_timers[HEAP0].time - _totalTicks);
                    }
                    if (hastimer) {
                        _wakeEvent.WaitOne(timeout);
                    }
                    else {
                        _wakeEvent.WaitOne();
                    }
                }
            }
        }

        private void UpdateTimers() {
            var nowTicks = DateTime.Now.Ticks;
            var deltaTicks = nowTicks - _prevTicks;
            if (_maximumFrameTicks > 0 && deltaTicks > _maximumFrameTicks) {
                deltaTicks = _maximumFrameTicks;
            }
            _totalTicks += deltaTicks;
            _prevTicks = nowTicks;

            lock (this) {
                while (_timerCount > 0 && _timers[HEAP0].time < _totalTicks) {
                    --_timerCount;
                    AddEvent(_timers[HEAP0]);
                    _timers[HEAP0] = new Event();
                    if (_timerCount > 0) {
                        _timers[HEAP0] = _timers[_timerCount + HEAP0];
                        downheap(_timers, _timerCount, HEAP0);
                    }
                }
            }
        }

        private void AddEvent(Event e) {
            if (_eventCount + 1 > _events.Length) {
                var cap = Math.Max(32, _eventCount + _eventCount / 2);
                var newbuf = new Event[cap];

                if (_eventStart != 0 && this._eventStart + _eventCount >= _events.Length) {
                    int lengthFromStart = _events.Length - _eventStart;
                    int lengthFromEnd = _eventCount - lengthFromStart;
                    Array.Copy(_events, _eventStart, newbuf, 0, lengthFromStart);
                    Array.Copy(_events, 0, newbuf, lengthFromStart, lengthFromEnd);
                }
                else {
                    Array.Copy(_events, _eventStart, newbuf, 0, _eventCount);
                }

                _events = newbuf;
                _eventStart = 0;
            }

            _events[(_eventStart + _eventCount) % _events.Length] = e;
            _eventCount++;
        }


        #region Event Heap

        const int HEAP0 = 1;

        static void downheap(Event[] heap, int N, int k) {
            Event he = heap[k];
            while (true) {
                int c = k << 1;

                if (c >= N + HEAP0)
                    break;

                c += c + 1 < N + HEAP0 && heap[c].time > heap[c + 1].time ? 1 : 0;

                if (he.time <= heap[c].time)
                    break;

                heap[k] = heap[c];
                k = c;
            }

            heap[k] = he;
        }

        static void upheap(Event[] heap, int k) {
            Event he = heap[k];

            while (true) {
                int p = k >> 1;

                if (p == 0 || heap[p].time < he.time)
                    break;

                heap[k] = heap[p];
                k = p;
            }

            heap[k] = he;
        }

        #endregion
    }

    /// <summary>
    /// Task scheduler for a event loop
    /// </summary>
    public class EventLoopTaskScheduler : TaskScheduler {
        private EventLoop eventLoop;

        /// <summary>
        /// Constructs a EventLoopTaskScheduler
        /// </summary>
        public EventLoopTaskScheduler(EventLoop eventLoop) {
            if (eventLoop == null) {
                throw new ArgumentNullException("eventLoop");
            }
            this.eventLoop = eventLoop;
        }

        protected override void QueueTask(Task task) {
            eventLoop.Post(PostCallback, task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) {
            if (SynchronizationContext.Current == eventLoop) {
                return TryExecuteTask(task);
            }
            else {
                return false;
            }
        }

        // not implemented
        protected override IEnumerable<Task> GetScheduledTasks() {
            return null;
        }

        public override int MaximumConcurrencyLevel {
            get {
                return 1;
            }
        }

        // this is where the actual task invocation occures
        private void PostCallback(object obj) {
            TryExecuteTask((Task)obj);
        }
    }
}

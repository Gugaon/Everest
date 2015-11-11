//--------------------------------------------------------------------------
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File: LimitedConcurrencyLevelTaskScheduler.cs
//
//--------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Monitor = System.Threading.Monitor;

namespace System.Threading.Tasks.Schedulers
{
	// ORIGINAL CODE FROM https://msdn.microsoft.com/en-us/library/ee789351(v=vs.110).aspx
	// Provides a task scheduler that ensures a maximum concurrency level while
	// running on top of the thread pool.
	public class LimitedConcurrencyLevelTaskScheduler : TaskScheduler, IDynamicConcurrencyLevelScheduler
	{
		// Indicates whether the current thread is processing work items.
		[ThreadStatic]
		private static bool _currentThreadIsProcessingItems;

		// The list of tasks to be executed
		private readonly LinkedList<Task> _tasks = new LinkedList<Task>(); // protected by lock(_tasks)

		// The maximum concurrency level allowed by this scheduler.
		private long _maxDegreeOfParallelism;

		// Indicates whether the scheduler is currently processing work items.
		private int _delegatesQueuedOrRunning = 0;

		// Creates a new instance with the specified degree of parallelism.
		public LimitedConcurrencyLevelTaskScheduler(int maxDegreeOfParallelism)
		{
			UpdateMaximumConcurrencyLevel(maxDegreeOfParallelism);
		}

		// Queues a task to the scheduler.
		protected sealed override void QueueTask(Task task)
		{
			// Add the task to the list of tasks to be processed.  If there aren't enough
			// delegates currently queued or running to process tasks, schedule another.
			lock (_tasks)
			{
				_tasks.AddLast(task);

				if (_delegatesQueuedOrRunning < MaximumConcurrencyLevel)
				{
					++_delegatesQueuedOrRunning;
					NotifyThreadPoolOfPendingWork();
				}
			}
		}

		public void RemovePendingTasks()
		{
			lock (_tasks)
			{
				while (true)
				{
					// When there are no more items to be processed,
					// note that we're done processing, and get out.
					if (_tasks.Count == 0)
						break;

					// Get the next item from the queue
					Task task = _tasks.First.Value;
					switch (task.Status)
					{
						// The task has been initialized but has not yet been scheduled.
						case TaskStatus.Created:
						// The task is waiting to be activated and scheduled internally.
						case TaskStatus.WaitingForActivation:
						// The task has been scheduled for execution but has not yet begun executing.
						case TaskStatus.WaitingToRun:
							_tasks.RemoveFirst();
							break;
						case TaskStatus.Running:
						case TaskStatus.RanToCompletion:
						case TaskStatus.Faulted:
						case TaskStatus.Canceled:
						case TaskStatus.WaitingForChildrenToComplete:
							// Do nothing. The task execution just finishes or is underway.
							break;
					}
				}
			}
		}

		// Inform the ThreadPool that there's work to be executed for this scheduler.
		private void NotifyThreadPoolOfPendingWork()
		{
			ThreadPool.UnsafeQueueUserWorkItem(_ =>
			{
				// Note that the current thread is now processing work items.
				// This is necessary to enable inlining of tasks into this thread.
				_currentThreadIsProcessingItems = true;
				try
				{
					// Process all available items in the queue.
					while (true)
					{
						Task item;
						lock (_tasks)
						{
							// When there are no more items to be processed,
							// note that we're done processing, and get out.
							if (_tasks.Count == 0)
							{
								--_delegatesQueuedOrRunning;
								break;
							}

							// Get the next item from the queue
							item = _tasks.First.Value;
							_tasks.RemoveFirst();
						}

						// Execute the task we pulled out of the queue
						base.TryExecuteTask(item);
					}
				}
				// We're done processing items on the current thread
				finally
				{
					_currentThreadIsProcessingItems = false;
				}
			}, null);
		}

		// Attempts to execute the specified task on the current thread.
		protected sealed override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
		{
			// If this thread isn't already processing a task, we don't support inlining
			if (!_currentThreadIsProcessingItems)
				return false;

			// If the task was previously queued, remove it from the queue
			if (taskWasPreviouslyQueued)
			{
				// Try to run the task.
				if (TryDequeue(task))
					return base.TryExecuteTask(task);
				else
					return false;
			}
			else
			{
				return base.TryExecuteTask(task);
			}
		}

		// Attempt to remove a previously scheduled task from the scheduler.
		protected sealed override bool TryDequeue(Task task)
		{
			lock (_tasks)
				return _tasks.Remove(task);
		}

		// Gets the maximum concurrency level supported by this scheduler.
		public sealed override int MaximumConcurrencyLevel
		{
			get
			{
				return (int)Interlocked.Read(ref _maxDegreeOfParallelism);
			}
		}

		public void UpdateMaximumConcurrencyLevel(int value)
		{
			if (value < 1)
				throw new ArgumentOutOfRangeException("maxDegreeOfParallelism");
			Interlocked.Exchange(ref _maxDegreeOfParallelism, value);
		}

		// Gets an enumerable of the tasks currently scheduled on this scheduler.
		protected sealed override IEnumerable<Task> GetScheduledTasks()
		{
			bool lockTaken = false;
			try
			{
				Monitor.TryEnter(_tasks, ref lockTaken);
				if (lockTaken)
					return _tasks;
				else
					throw new NotSupportedException();
			}
			finally
			{
				if (lockTaken)
					Monitor.Exit(_tasks);
			}
		}
	}
	// The following is a portion of the output from a single run of the example:
	//    'T' in task t1-4 on thread 3   'U' in task t1-4 on thread 3   'V' in task t1-4 on thread 3
	//    'W' in task t1-4 on thread 3   'X' in task t1-4 on thread 3   'Y' in task t1-4 on thread 3
	//    'Z' in task t1-4 on thread 3   '[' in task t1-4 on thread 3   '\' in task t1-4 on thread 3
	//    ']' in task t1-4 on thread 3   '^' in task t1-4 on thread 3   '_' in task t1-4 on thread 3
	//    '`' in task t1-4 on thread 3   'a' in task t1-4 on thread 3   'b' in task t1-4 on thread 3
	//    'c' in task t1-4 on thread 3   'd' in task t1-4 on thread 3   'e' in task t1-4 on thread 3
	//    'f' in task t1-4 on thread 3   'g' in task t1-4 on thread 3   'h' in task t1-4 on thread 3
	//    'i' in task t1-4 on thread 3   'j' in task t1-4 on thread 3   'k' in task t1-4 on thread 3
	//    'l' in task t1-4 on thread 3   'm' in task t1-4 on thread 3   'n' in task t1-4 on thread 3
	//    'o' in task t1-4 on thread 3   'p' in task t1-4 on thread 3   ']' in task t1-2 on thread 4
	//    '^' in task t1-2 on thread 4   '_' in task t1-2 on thread 4   '`' in task t1-2 on thread 4
	//    'a' in task t1-2 on thread 4   'b' in task t1-2 on thread 4   'c' in task t1-2 on thread 4
	//    'd' in task t1-2 on thread 4   'e' in task t1-2 on thread 4   'f' in task t1-2 on thread 4
	//    'g' in task t1-2 on thread 4   'h' in task t1-2 on thread 4   'i' in task t1-2 on thread 4
	//    'j' in task t1-2 on thread 4   'k' in task t1-2 on thread 4   'l' in task t1-2 on thread 4
	//    'm' in task t1-2 on thread 4   'n' in task t1-2 on thread 4   'o' in task t1-2 on thread 4
	//    'p' in task t1-2 on thread 4   'q' in task t1-2 on thread 4   'r' in task t1-2 on thread 4
	//    's' in task t1-2 on thread 4   't' in task t1-2 on thread 4   'u' in task t1-2 on thread 4
	//    'v' in task t1-2 on thread 4   'w' in task t1-2 on thread 4   'x' in task t1-2 on thread 4
	//    'y' in task t1-2 on thread 4   'z' in task t1-2 on thread 4   '{' in task t1-2 on thread 4
	//    '|' in task t1-2 on thread 4   '}' in task t1-2 on thread 4   '~' in task t1-2 on thread 4
	//    'q' in task t1-4 on thread 3   'r' in task t1-4 on thread 3   's' in task t1-4 on thread 3
	//    't' in task t1-4 on thread 3   'u' in task t1-4 on thread 3   'v' in task t1-4 on thread 3
	//    'w' in task t1-4 on thread 3   'x' in task t1-4 on thread 3   'y' in task t1-4 on thread 3
	//    'z' in task t1-4 on thread 3   '{' in task t1-4 on thread 3   '|' in task t1-4 on thread 3
}

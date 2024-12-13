//
// Copyright (c) 2024 Erik Alveflo
//
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpExample
{
	internal static class TaskUtils
	{
		public static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
		{
			using (var delayCts = new CancellationTokenSource(timeout))
			{
				var delayTask = Task.Delay(Timeout.Infinite, delayCts.Token);
				var completedTask = await Task.WhenAny(task, delayTask);
				if (completedTask == task)
				{
					delayCts.Cancel(); // Cancel `delayTask` to avoid leak
					return await task; // Finishes immediately
				}
				throw new TimeoutException();
			}
		}

		public static async Task WithTimeout(Task task, TimeSpan timeout)
		{
			async Task<int> WithIntResult(Task t)
			{
				await t;
				return 0;
			}
			await WithTimeout(WithIntResult(task), timeout);
		}
	}
}

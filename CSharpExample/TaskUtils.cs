using System;
using System.Threading;
using System.Threading.Tasks;

namespace CSharpExample
{
	internal static class TaskUtils
	{
		public static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
		{
			using (var cts = new CancellationTokenSource(timeout))
			{
				var delayTask = Task.Delay(Timeout.Infinite, cts.Token);
				var completedTask = await Task.WhenAny(task, delayTask);
				if (completedTask == task)
				{
					return await task;
				}
				cts.Cancel(); // Cancel delayTask
				throw new TimeoutException();
			}
		}
	}
}

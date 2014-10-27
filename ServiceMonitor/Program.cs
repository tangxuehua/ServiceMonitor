using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace ServiceMonitor
{
    class Program
    {
        static IEnumerable<string> _serviceList;
        static readonly int _detectPeriod = int.Parse(ConfigurationManager.AppSettings["detectPeriod"]);
        static readonly int _serviceStartTimeout = int.Parse(ConfigurationManager.AppSettings["serviceStartTimeout"]);
        static readonly ScheduleService _scheduleService = new ScheduleService();

        static void Main(string[] args)
        {
            _serviceList = ConfigurationManager.AppSettings["serviceList"].Split(',');
            _scheduleService.ScheduleTask("MonitorServiceStatus", MonitorServiceStatus, _detectPeriod, _detectPeriod);
            Console.WriteLine("{0} - Service monitor started, press any key to exit.", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.ReadLine();
        }

        static void MonitorServiceStatus()
        {
            foreach (var service in _serviceList)
            {
                var serviceRunning = Process.GetProcessesByName(service).Count() > 0;
                if (!serviceRunning)
                {
                    Console.WriteLine("{0} - '{1}' is stopped, try to restart it.", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), service);
                    RestartService(service);
                }
            }
        }
        static void RestartService(string service)
        {
            try
            {
                var serviceController = new ServiceController(service);
                serviceController.Start();
                serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMilliseconds(_serviceStartTimeout));
                Console.WriteLine("{0} - '{1}' restart successfully.", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), service);
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0} - Restart service '{1}' failed, exception:{1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), service, ex);
            }
        }
    }

    class ScheduleService
    {
        private readonly ConcurrentDictionary<int, TimerBasedTask> _taskDict = new ConcurrentDictionary<int, TimerBasedTask>();
        private int _maxTaskId;

        public int ScheduleTask(string actionName, Action action, int dueTime, int period)
        {
            var newTaskId = Interlocked.Increment(ref _maxTaskId);
            var timer = new Timer((obj) =>
            {
                var currentTaskId = (int)obj;
                TimerBasedTask currentTask;
                if (_taskDict.TryGetValue(currentTaskId, out currentTask))
                {
                    if (currentTask.Stoped)
                    {
                        return;
                    }

                    try
                    {
                        currentTask.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                        if (currentTask.Stoped)
                        {
                            return;
                        }
                        currentTask.Action();
                    }
                    catch (ObjectDisposedException) { }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Task has exception, actionName:{0}, dueTime:{1}, period:{2}, exception:{3}", currentTask.ActionName, currentTask.DueTime, currentTask.Period, ex);
                    }
                    finally
                    {
                        if (!currentTask.Stoped)
                        {
                            try
                            {
                                currentTask.Timer.Change(currentTask.Period, currentTask.Period);
                            }
                            catch (ObjectDisposedException) { }
                        }
                    }
                }
            }, newTaskId, Timeout.Infinite, Timeout.Infinite);

            if (!_taskDict.TryAdd(newTaskId, new TimerBasedTask { ActionName = actionName, Action = action, Timer = timer, DueTime = dueTime, Period = period, Stoped = false }))
            {
                Console.WriteLine("Schedule task failed, actionName:{0}, dueTime:{1}, period:{2}", actionName, dueTime, period);
                return -1;
            }

            timer.Change(dueTime, period);

            return newTaskId;
        }
        public void ShutdownTask(int taskId)
        {
            TimerBasedTask task;
            if (_taskDict.TryRemove(taskId, out task))
            {
                task.Stoped = true;
                task.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                task.Timer.Dispose();
            }
        }

        class TimerBasedTask
        {
            public string ActionName;
            public Action Action;
            public Timer Timer;
            public int DueTime;
            public int Period;
            public bool Stoped;
        }
    }
}

/* 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System.Configuration.Install;
using System.Reflection;

namespace Teltec.Everest.Scheduler
{
	public static class ServiceHelper
	{
		//static ServiceProcessInstaller ProcessInstaller;
		//static System.ServiceProcess.ServiceInstaller ServiceInstaller;

		public static void SelfStart(bool run = false)
		{
			string serviceName = Assembly.GetExecutingAssembly().GetName().Name;

			ServiceInstaller installer = new ServiceInstaller(serviceName);
			installer.StartService();
		}

		public static void SelfInstall(bool run = false)
		{
			ManagedInstallerClass.InstallHelper(new string[] { Assembly.GetExecutingAssembly().Location });

			//string servicePath = Assembly.GetExecutingAssembly().Location;
			//string serviceName = Assembly.GetExecutingAssembly().GetName().Name;
			//
			//ServiceInstaller installer = new ServiceInstaller(serviceName);
			//installer.DesiredAccess = ServiceAccessRights.AllAccess;
			//installer.BinaryPath = servicePath;
			//installer.DependsOn = new[] {
			//	"MSSQL$SQLEXPRESS"
			//	//"MSSQLSERVER"
			//};
			//
			//installer.InstallAndStart();
		}

		public static void SelfUninstall()
		{
			ManagedInstallerClass.InstallHelper(new string[] { "/u", Assembly.GetExecutingAssembly().Location });

			//string serviceName = Assembly.GetExecutingAssembly().GetName().Name;

			//ServiceInstaller installer = new ServiceInstaller(serviceName);
			//installer.Uninstall();
		}
	}
}

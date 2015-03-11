﻿using System;
using NHibernate.Tool.hbm2ddl;
using NUnit.Framework;
using Teltec.Backup.App.DAO.NHibernate;

namespace Teltec.Backup.App.DAO.Test
{
	[TestFixture]
	public class SchemaTest
	{
		[Test]
		public void CanGenerateSchema()
		{
			var schemaUpdate = new SchemaUpdate(NHibernateHelper.Configuration);
			schemaUpdate.Execute(Console.WriteLine, true);
		}
	}
}
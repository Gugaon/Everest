# Teltec Cloud Backup

## Installation

- After you install, you need to change the SQL Server authentication mode to "SQL Server and Windows Authentication mode". We may automate this in the future, but for now you have to do it manually.

## Features

- Primarily designed for Amazon S3 and Windows;
- Backup and restore plans;
- Scheduled execution of backup and restore plans;
- Synchronization;
- Does not support long paths (with more than 260 characters - Windows' MAX_PATH) -- Currently limited by lack of support from the AWSSDK - see https://github.com/aws/aws-sdk-net/issues/294;
- Automatic network shares mapping;
- Runs backup/restore operations without requiring an active user session (a logged user);
- Automatically deletes old backuped files;

## Changelog

You can read the entire changelog [here](CHANGELOG.md).

## Known Problems

- \#4: FileSystemTreeView: unchecking a parent node without expanding it doesn't uncheck its sub-nodes;
- \#8: Task scheduler does not DELETE existing tasks for plans that no longer exist;
- \#14: Editing a plan and changing its storage account won't work properly.
- Can't run the GUI in more than one user session simultaneously;

## Future Work

- Upload/Download retry policy (with exponential backoff) - See http://docs.aws.amazon.com/general/latest/gr/api-retries.html
- Automatically send reports upon failure or completion;
- Improve concurrency;
- Limit bandwidth;
- Restore files to a specified point in time;
- Add lifecycle rules to transition to Glacier after a configurable amount of days - Note that object restoration from an archive can take up to five hours;
- Attempt to make abstractions more developer friendly;

## Licensing Policy

We try our best to comply with all requirements imposed by the licenses we directly or indirectly use.
If you believe we have infringed a license, please, let us know by opening an issue in our GitHub repository or contacting us directly.

CC-BY-SA-3.0
	[Creative Commons Wiki - Best practices for attribution](https://wiki.creativecommons.org/wiki/Best_practices_for_attribution#Examples_of_attribution)
	[GPL compatibility use cases - Sharing computer code between GPL software and CC-BY-SA communities](https://wiki.creativecommons.org/wiki/GPL_compatibility_use_cases#Sharing_computer_code_between_GPL_software_and_CC-BY-SA_communities)
	[Stack Exchange Blog - Attribution Required](https://blog.stackexchange.com/2009/06/attribution-required/)
Microsoft
	Code samples from MSDN - [Microsoft Developer Services Agreement](https://msdn.microsoft.com/en-us/cc300389.aspx#D) requires [Microsoft Limited Public License](http://opensource.org/licenses/MS-PL)
AWS
	[AWS Site Terms](http://aws.amazon.com/terms/)

## License

TODO

## Copyright

Copyright (C) 2015 Teltec Solutions Ltda.

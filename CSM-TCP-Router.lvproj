<?xml version='1.0' encoding='UTF-8'?>
<Project Type="Project" LVVersion="20008000">
	<Property Name="NI.LV.All.SourceOnly" Type="Bool">true</Property>
	<Item Name="My Computer" Type="My Computer">
		<Property Name="server.app.propertiesEnabled" Type="Bool">true</Property>
		<Property Name="server.control.propertiesEnabled" Type="Bool">true</Property>
		<Property Name="server.tcp.enabled" Type="Bool">false</Property>
		<Property Name="server.tcp.port" Type="Int">0</Property>
		<Property Name="server.tcp.serviceName" Type="Str">My Computer/VI Server</Property>
		<Property Name="server.tcp.serviceName.default" Type="Str">My Computer/VI Server</Property>
		<Property Name="server.vi.callsEnabled" Type="Bool">true</Property>
		<Property Name="server.vi.propertiesEnabled" Type="Bool">true</Property>
		<Property Name="specify.custom.address" Type="Bool">false</Property>
		<Item Name="Client" Type="Folder" URL="../Client">
			<Property Name="NI.DISK" Type="Bool">true</Property>
		</Item>
		<Item Name="CSM_Modules" Type="Folder" URL="../CSM_Modules">
			<Property Name="NI.DISK" Type="Bool">true</Property>
		</Item>
		<Item Name="Server" Type="Folder" URL="../Server">
			<Property Name="NI.DISK" Type="Bool">true</Property>
		</Item>
		<Item Name="csm-app.ini" Type="Document" URL="../csm-app.ini"/>
		<Item Name="CSM-TCP-Router.aliases" Type="Document" URL="../CSM-TCP-Router.aliases"/>
		<Item Name="Dependencies" Type="Dependencies">
			<Item Name="user.lib" Type="Folder">
				<Item Name="_csm_user_lib_dir.vi" Type="VI" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_scripts/_csm_user_lib_dir.vi"/>
				<Item Name="API String Arguments Support.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/addons/API-String-Arugments-Support/API String Arguments Support.lvlib"/>
				<Item Name="Build Error Cluster__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/error/error.llb/Build Error Cluster__ogtk.vi"/>
				<Item Name="Communicable State Machine.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/Communicable State Machine.lvlib"/>
				<Item Name="Convert File Extension (Path)__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/file/file.llb/Convert File Extension (Path)__ogtk.vi"/>
				<Item Name="Convert File Extension (String)__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/file/file.llb/Convert File Extension (String)__ogtk.vi"/>
				<Item Name="Convert File Extension__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/file/file.llb/Convert File Extension__ogtk.vi"/>
				<Item Name="CSM-Helper Utility.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/CSM-Helper Utility.lvlib"/>
				<Item Name="CSM-Helper.lvclass" Type="LVClass" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/CSM-Helper.lvclass"/>
				<Item Name="csmHelper-CaseStructure.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/CaseStructure/csmHelper-CaseStructure.lvlib"/>
				<Item Name="csmHelper-Diagram.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/Diagram/csmHelper-Diagram.lvlib"/>
				<Item Name="csmHelper-PairedTunnels.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/PairedTunnels/csmHelper-PairedTunnels.lvlib"/>
				<Item Name="csmHelper-Relationship.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/Relationship/csmHelper-Relationship.lvlib"/>
				<Item Name="csmHelper-SubVI.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/SubVI/csmHelper-SubVI.lvlib"/>
				<Item Name="csmHelper-Terminals.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/Terminals/csmHelper-Terminals.lvlib"/>
				<Item Name="GSTOP.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/GlobalStop/GSTOP.lvlib"/>
				<Item Name="INI-Static-Variable-Support.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/addons/INI-Static-Variable-Support/INI-Static-Variable-Support.lvlib"/>
				<Item Name="LabVIEW-Config.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/addons/INI-Static-Variable-Support/LabVIEW-Config/LabVIEW-Config.lvlib"/>
				<Item Name="lvcsm File Support.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/lvcsmFile/lvcsm File Support.lvlib"/>
				<Item Name="Tick Count (ms)__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/time/time.llb/Tick Count (ms)__ogtk.vi"/>
				<Item Name="Wait (ms)__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/time/time.llb/Wait (ms)__ogtk.vi"/>
			</Item>
			<Item Name="vi.lib" Type="Folder">
				<Item Name="8.6CompatibleGlobalVar.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/config.llb/8.6CompatibleGlobalVar.vi"/>
				<Item Name="Acquire Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Acquire Semaphore.vi"/>
				<Item Name="Add State(s) to Queue__JKI_lib_State_Machine.vi" Type="VI" URL="/&lt;vilib&gt;/addons/_JKI Toolkits/State Machine/_JKI_lib_State_Machine.llb/Add State(s) to Queue__JKI_lib_State_Machine.vi"/>
				<Item Name="AddNamedSemaphorePrefix.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/AddNamedSemaphorePrefix.vi"/>
				<Item Name="Application Directory.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/file.llb/Application Directory.vi"/>
				<Item Name="Build State String with Arguments__JKI_lib_State_Machine.vi" Type="VI" URL="/&lt;vilib&gt;/addons/_JKI Toolkits/State Machine/_JKI_lib_State_Machine.llb/Build State String with Arguments__JKI_lib_State_Machine.vi"/>
				<Item Name="BuildHelpPath.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/BuildHelpPath.vi"/>
				<Item Name="Check if File or Folder Exists.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/libraryn.llb/Check if File or Folder Exists.vi"/>
				<Item Name="Check Special Tags.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Check Special Tags.vi"/>
				<Item Name="Clear Errors.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Clear Errors.vi"/>
				<Item Name="Convert property node font to graphics font.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Convert property node font to graphics font.vi"/>
				<Item Name="Create Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Create Semaphore.vi"/>
				<Item Name="DateTime.ISO8601.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/DateTime.ISO8601.lvclass"/>
				<Item Name="DateTimeFormatter.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter/DateTimeFormatter.lvclass"/>
				<Item Name="Destroy Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Destroy Semaphore.vi"/>
				<Item Name="Details Display Dialog.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Details Display Dialog.vi"/>
				<Item Name="DialogType.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/DialogType.ctl"/>
				<Item Name="DialogTypeEnum.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/DialogTypeEnum.ctl"/>
				<Item Name="Epoch.GPS.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/epoch.GPS/Epoch.GPS.lvclass"/>
				<Item Name="Epoch.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/epoch/Epoch.lvclass"/>
				<Item Name="Epoch.UNIX.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/epoch.UNIX/Epoch.UNIX.lvclass"/>
				<Item Name="Epoch.UTC.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/epoch.UTC/Epoch.UTC.lvclass"/>
				<Item Name="EpochNotTimestamp.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/EpochNotTimestamp.vi"/>
				<Item Name="Epochs -- enum.ctl" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/Epochs -- enum.ctl"/>
				<Item Name="Error Cluster From Error Code.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Error Cluster From Error Code.vi"/>
				<Item Name="Error Code Database.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Error Code Database.vi"/>
				<Item Name="ErrWarn.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/ErrWarn.ctl"/>
				<Item Name="eventvkey.ctl" Type="VI" URL="/&lt;vilib&gt;/event_ctls.llb/eventvkey.ctl"/>
				<Item Name="ex_CorrectErrorChain.vi" Type="VI" URL="/&lt;vilib&gt;/express/express shared/ex_CorrectErrorChain.vi"/>
				<Item Name="Find Tag.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Find Tag.vi"/>
				<Item Name="Format Message String.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Format Message String.vi"/>
				<Item Name="General Error Handler Core CORE.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/General Error Handler Core CORE.vi"/>
				<Item Name="General Error Handler.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/General Error Handler.vi"/>
				<Item Name="Get Local UTC Offset.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/Get Local UTC Offset.vi"/>
				<Item Name="Get Semaphore Status.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Get Semaphore Status.vi"/>
				<Item Name="Get String Text Bounds.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Get String Text Bounds.vi"/>
				<Item Name="Get Text Rect.vi" Type="VI" URL="/&lt;vilib&gt;/picture/picture.llb/Get Text Rect.vi"/>
				<Item Name="GetHelpDir.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/GetHelpDir.vi"/>
				<Item Name="GetNamedSemaphorePrefix.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/GetNamedSemaphorePrefix.vi"/>
				<Item Name="GetRTHostConnectedProp.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/GetRTHostConnectedProp.vi"/>
				<Item Name="GregorianCal_MonthLengths.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/GregorianCal_MonthLengths.vi"/>
				<Item Name="Is Path and Not Empty.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/file.llb/Is Path and Not Empty.vi"/>
				<Item Name="isDST.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/isDST.vi"/>
				<Item Name="isLeapYear (integer).vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/isLeapYear (integer).vi"/>
				<Item Name="isLeapYear (timestamp).vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/isLeapYear (timestamp).vi"/>
				<Item Name="ISO Day -- enum.ctl" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/ISO Day -- enum.ctl"/>
				<Item Name="ISO8601 Date Formats.ctl" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/ISO8601 Date Formats.ctl"/>
				<Item Name="ISO8601 DateString to Timestamp.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/ISO8601 DateString to Timestamp.vi"/>
				<Item Name="JKI TCP Server.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/addons/_JKI.lib/_JKI_lib_TCP_Server.llb/JKI TCP Server.lvclass"/>
				<Item Name="Longest Line Length in Pixels.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Longest Line Length in Pixels.vi"/>
				<Item Name="LVBoundsTypeDef.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVBoundsTypeDef.ctl"/>
				<Item Name="LVDateTimeRec.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVDateTimeRec.ctl"/>
				<Item Name="LVPointTypeDef.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVPointTypeDef.ctl"/>
				<Item Name="LVPositionTypeDef.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVPositionTypeDef.ctl"/>
				<Item Name="LVRectTypeDef.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVRectTypeDef.ctl"/>
				<Item Name="NI_Data Type.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/Data Type/NI_Data Type.lvlib"/>
				<Item Name="NI_FileType.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/lvfile.llb/NI_FileType.lvlib"/>
				<Item Name="NI_LVConfig.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/config.llb/NI_LVConfig.lvlib"/>
				<Item Name="NI_PackedLibraryUtility.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/LVLibp/NI_PackedLibraryUtility.lvlib"/>
				<Item Name="Not A Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Not A Semaphore.vi"/>
				<Item Name="Not Found Dialog.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Not Found Dialog.vi"/>
				<Item Name="Obtain Semaphore Reference.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Obtain Semaphore Reference.vi"/>
				<Item Name="Parse State Queue__JKI_lib_State_Machine.vi" Type="VI" URL="/&lt;vilib&gt;/addons/_JKI Toolkits/State Machine/_JKI_lib_State_Machine.llb/Parse State Queue__JKI_lib_State_Machine.vi"/>
				<Item Name="Release Semaphore Reference.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Release Semaphore Reference.vi"/>
				<Item Name="Release Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Release Semaphore.vi"/>
				<Item Name="Release Semaphore_71.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Release Semaphore_71.vi"/>
				<Item Name="RemoveNamedSemaphorePrefix.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/RemoveNamedSemaphorePrefix.vi"/>
				<Item Name="Search and Replace Pattern.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Search and Replace Pattern.vi"/>
				<Item Name="Semaphore Name &amp; Ref DB Action.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Semaphore Name &amp; Ref DB Action.ctl"/>
				<Item Name="Semaphore Name &amp; Ref DB.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Semaphore Name &amp; Ref DB.vi"/>
				<Item Name="Semaphore RefNum" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Semaphore RefNum"/>
				<Item Name="Semaphore Refnum Core.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Semaphore Refnum Core.ctl"/>
				<Item Name="Set Bold Text.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Set Bold Text.vi"/>
				<Item Name="Set String Value.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Set String Value.vi"/>
				<Item Name="Simple Error Handler.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Simple Error Handler.vi"/>
				<Item Name="Space Constant.vi" Type="VI" URL="/&lt;vilib&gt;/dlg_ctls.llb/Space Constant.vi"/>
				<Item Name="subFile Dialog.vi" Type="VI" URL="/&lt;vilib&gt;/express/express input/FileDialogBlock.llb/subFile Dialog.vi"/>
				<Item Name="Synchronize Data Flow.vim" Type="VI" URL="/&lt;vilib&gt;/Utility/Synchronize Data Flow.vim"/>
				<Item Name="TagReturnType.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/TagReturnType.ctl"/>
				<Item Name="Three Button Dialog CORE.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Three Button Dialog CORE.vi"/>
				<Item Name="Three Button Dialog.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Three Button Dialog.vi"/>
				<Item Name="Timestamp to ISO8601 Date.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 Date.vi"/>
				<Item Name="Timestamp to ISO8601 Day.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 Day.vi"/>
				<Item Name="Timestamp to ISO8601 UTC DateTime.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 UTC DateTime.vi"/>
				<Item Name="Timestamp to ISO8601 Week.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 Week.vi"/>
				<Item Name="Timestamp to ISO8601 Year.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 Year.vi"/>
				<Item Name="TRef Traverse.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/traverseref.llb/TRef Traverse.vi"/>
				<Item Name="TRef TravTarget.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/traverseref.llb/TRef TravTarget.ctl"/>
				<Item Name="Trim Whitespace.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Trim Whitespace.vi"/>
				<Item Name="UTC Offset to Seconds.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/UTC Offset to Seconds.vi"/>
				<Item Name="UTC Offsets -- enum.ctl" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/UTC Offsets -- enum.ctl"/>
				<Item Name="Validate Semaphore Size.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Validate Semaphore Size.vi"/>
				<Item Name="VI Scripting - Traverse.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/traverseref.llb/VI Scripting - Traverse.lvlib"/>
				<Item Name="whitespace.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/whitespace.ctl"/>
			</Item>
		</Item>
		<Item Name="Build Specifications" Type="Build">
			<Property Name="NI.SortType" Type="Int">3</Property>
			<Item Name="Client" Type="EXE">
				<Property Name="App_copyErrors" Type="Bool">true</Property>
				<Property Name="App_INI_aliasGUID" Type="Str">{C742BCF0-8AA9-4048-8605-2097FA187F0D}</Property>
				<Property Name="App_INI_GUID" Type="Str">{21C4E170-765E-499C-9C8F-D876026AFFBE}</Property>
				<Property Name="App_serverConfig.httpPort" Type="Int">8002</Property>
				<Property Name="App_serverType" Type="Int">0</Property>
				<Property Name="Bld_autoIncrement" Type="Bool">true</Property>
				<Property Name="Bld_buildCacheID" Type="Str">{93A7E0BF-4F62-4B03-8793-D8003265B7FB}</Property>
				<Property Name="Bld_buildSpecName" Type="Str">Client</Property>
				<Property Name="Bld_excludeInlineSubVIs" Type="Bool">true</Property>
				<Property Name="Bld_excludeLibraryItems" Type="Bool">true</Property>
				<Property Name="Bld_excludePolymorphicVIs" Type="Bool">true</Property>
				<Property Name="Bld_localDestDir" Type="Path">../_build/PC</Property>
				<Property Name="Bld_localDestDirType" Type="Str">relativeToProject</Property>
				<Property Name="Bld_modifyLibraryFile" Type="Bool">true</Property>
				<Property Name="Bld_previewCacheID" Type="Str">{3A907983-FA2B-4A7C-9433-F96A748C309E}</Property>
				<Property Name="Bld_version.build" Type="Int">9</Property>
				<Property Name="Bld_version.major" Type="Int">1</Property>
				<Property Name="Destination[0].destName" Type="Str">Client.exe</Property>
				<Property Name="Destination[0].path" Type="Path">../_build/PC/Client.exe</Property>
				<Property Name="Destination[0].path.type" Type="Str">relativeToProject</Property>
				<Property Name="Destination[0].preserveHierarchy" Type="Bool">true</Property>
				<Property Name="Destination[0].type" Type="Str">App</Property>
				<Property Name="Destination[1].destName" Type="Str">Support Directory</Property>
				<Property Name="Destination[1].path" Type="Path">../_build/PC/data</Property>
				<Property Name="Destination[1].path.type" Type="Str">relativeToProject</Property>
				<Property Name="DestinationCount" Type="Int">2</Property>
				<Property Name="Source[0].itemID" Type="Str">{48CFFAE0-C0EA-4334-A808-0D9259B39FFE}</Property>
				<Property Name="Source[0].type" Type="Str">Container</Property>
				<Property Name="Source[1].destinationIndex" Type="Int">0</Property>
				<Property Name="Source[1].itemID" Type="Ref">/My Computer/Client/Client.vi</Property>
				<Property Name="Source[1].sourceInclusion" Type="Str">TopLevel</Property>
				<Property Name="Source[1].type" Type="Str">VI</Property>
				<Property Name="SourceCount" Type="Int">2</Property>
				<Property Name="TgtF_fileDescription" Type="Str">Client</Property>
				<Property Name="TgtF_internalName" Type="Str">Client</Property>
				<Property Name="TgtF_legalCopyright" Type="Str">Copyright ?2024 </Property>
				<Property Name="TgtF_productName" Type="Str">Client</Property>
				<Property Name="TgtF_targetfileGUID" Type="Str">{0C87AE52-55EC-44A3-9C4A-8476F95F7E7A}</Property>
				<Property Name="TgtF_targetfileName" Type="Str">Client.exe</Property>
				<Property Name="TgtF_versionIndependent" Type="Bool">true</Property>
			</Item>
			<Item Name="Server" Type="EXE">
				<Property Name="App_INI_aliasGUID" Type="Str">{7AA56991-BFE7-4912-87E0-3AA2B117FF42}</Property>
				<Property Name="App_INI_aliasItemID" Type="Ref">/My Computer/CSM-TCP-Router.aliases</Property>
				<Property Name="App_INI_GUID" Type="Str">{B6195192-70E8-43F8-971D-DC2DC6874282}</Property>
				<Property Name="App_INI_itemID" Type="Ref">/My Computer/csm-app.ini</Property>
				<Property Name="App_serverConfig.httpPort" Type="Int">8002</Property>
				<Property Name="App_serverType" Type="Int">0</Property>
				<Property Name="Bld_autoIncrement" Type="Bool">true</Property>
				<Property Name="Bld_buildCacheID" Type="Str">{33A88D54-BD07-4E91-A063-1B2C87AB06B2}</Property>
				<Property Name="Bld_buildSpecName" Type="Str">Server</Property>
				<Property Name="Bld_excludeInlineSubVIs" Type="Bool">true</Property>
				<Property Name="Bld_excludeLibraryItems" Type="Bool">true</Property>
				<Property Name="Bld_excludePolymorphicVIs" Type="Bool">true</Property>
				<Property Name="Bld_localDestDir" Type="Path">../_build/PC</Property>
				<Property Name="Bld_localDestDirType" Type="Str">relativeToProject</Property>
				<Property Name="Bld_modifyLibraryFile" Type="Bool">true</Property>
				<Property Name="Bld_previewCacheID" Type="Str">{A916FCA7-CB59-42EA-83BF-E2797FCA83AB}</Property>
				<Property Name="Bld_version.build" Type="Int">17</Property>
				<Property Name="Bld_version.major" Type="Int">1</Property>
				<Property Name="Destination[0].destName" Type="Str">Server.exe</Property>
				<Property Name="Destination[0].path" Type="Path">../_build/PC/Server.exe</Property>
				<Property Name="Destination[0].path.type" Type="Str">relativeToProject</Property>
				<Property Name="Destination[0].preserveHierarchy" Type="Bool">true</Property>
				<Property Name="Destination[0].type" Type="Str">App</Property>
				<Property Name="Destination[1].destName" Type="Str">Support Directory</Property>
				<Property Name="Destination[1].path" Type="Path">../_build/PC/data</Property>
				<Property Name="Destination[1].path.type" Type="Str">relativeToProject</Property>
				<Property Name="DestinationCount" Type="Int">2</Property>
				<Property Name="Source[0].itemID" Type="Str">{4994EE6A-0752-453B-8CAD-1F9ACB181C65}</Property>
				<Property Name="Source[0].type" Type="Str">Container</Property>
				<Property Name="Source[1].destinationIndex" Type="Int">0</Property>
				<Property Name="Source[1].itemID" Type="Ref">/My Computer/Server/CSM-TCP-Router(Server).vi</Property>
				<Property Name="Source[1].sourceInclusion" Type="Str">TopLevel</Property>
				<Property Name="Source[1].type" Type="Str">VI</Property>
				<Property Name="SourceCount" Type="Int">2</Property>
				<Property Name="TgtF_fileDescription" Type="Str">Server</Property>
				<Property Name="TgtF_internalName" Type="Str">Server</Property>
				<Property Name="TgtF_legalCopyright" Type="Str">Copyright ?2025 </Property>
				<Property Name="TgtF_productName" Type="Str">Server</Property>
				<Property Name="TgtF_targetfileGUID" Type="Str">{C5684D38-EE64-4EEA-9D5E-553BCF8219CE}</Property>
				<Property Name="TgtF_targetfileName" Type="Str">Server.exe</Property>
				<Property Name="TgtF_versionIndependent" Type="Bool">true</Property>
			</Item>
		</Item>
	</Item>
	<Item Name="RT PXI Target" Type="RT PXI Chassis">
		<Property Name="alias.name" Type="Str">RT PXI Target</Property>
		<Property Name="alias.value" Type="Str">10.144.36.41</Property>
		<Property Name="CCSymbols" Type="Str">TARGET_TYPE,RT;OS,PharLap;CPU,x86;</Property>
		<Property Name="host.ResponsivenessCheckEnabled" Type="Bool">true</Property>
		<Property Name="host.ResponsivenessCheckPingDelay" Type="UInt">5000</Property>
		<Property Name="host.ResponsivenessCheckPingTimeout" Type="UInt">1000</Property>
		<Property Name="host.TargetCPUID" Type="UInt">3</Property>
		<Property Name="host.TargetOSID" Type="UInt">15</Property>
		<Property Name="target.cleanupVisa" Type="Bool">false</Property>
		<Property Name="target.FPProtocolGlobals_ControlTimeLimit" Type="Int">300</Property>
		<Property Name="target.getDefault-&gt;WebServer.Port" Type="Int">80</Property>
		<Property Name="target.getDefault-&gt;WebServer.Timeout" Type="Int">60</Property>
		<Property Name="target.IOScan.Faults" Type="Str"></Property>
		<Property Name="target.IOScan.NetVarPeriod" Type="UInt">100</Property>
		<Property Name="target.IOScan.NetWatchdogEnabled" Type="Bool">false</Property>
		<Property Name="target.IOScan.Period" Type="UInt">10000</Property>
		<Property Name="target.IOScan.PowerupMode" Type="UInt">0</Property>
		<Property Name="target.IOScan.Priority" Type="UInt">0</Property>
		<Property Name="target.IOScan.ReportModeConflict" Type="Bool">true</Property>
		<Property Name="target.IsRemotePanelSupported" Type="Bool">true</Property>
		<Property Name="target.RTCPULoadMonitoringEnabled" Type="Bool">true</Property>
		<Property Name="target.RTDebugWebServerHTTPPort" Type="Int">8001</Property>
		<Property Name="target.RTTarget.ApplicationPath" Type="Path">/c/ni-rt/startup/startup.rtexe</Property>
		<Property Name="target.RTTarget.EnableFileSharing" Type="Bool">true</Property>
		<Property Name="target.RTTarget.IPAccess" Type="Str">+*</Property>
		<Property Name="target.RTTarget.LaunchAppAtBoot" Type="Bool">false</Property>
		<Property Name="target.RTTarget.VIPath" Type="Path">/c/ni-rt/startup</Property>
		<Property Name="target.server.app.propertiesEnabled" Type="Bool">true</Property>
		<Property Name="target.server.control.propertiesEnabled" Type="Bool">true</Property>
		<Property Name="target.server.tcp.access" Type="Str">+*</Property>
		<Property Name="target.server.tcp.enabled" Type="Bool">false</Property>
		<Property Name="target.server.tcp.paranoid" Type="Bool">true</Property>
		<Property Name="target.server.tcp.port" Type="Int">3363</Property>
		<Property Name="target.server.tcp.serviceName" Type="Str">Main Application Instance/VI Server</Property>
		<Property Name="target.server.tcp.serviceName.default" Type="Str">Main Application Instance/VI Server</Property>
		<Property Name="target.server.vi.access" Type="Str">+*</Property>
		<Property Name="target.server.vi.callsEnabled" Type="Bool">true</Property>
		<Property Name="target.server.vi.propertiesEnabled" Type="Bool">true</Property>
		<Property Name="target.WebServer.Config" Type="Str">Listen 8000

NI.ServerName default
DocumentRoot "$LVSERVER_DOCROOT"
TypesConfig "$LVSERVER_CONFIGROOT/mime.types"
DirectoryIndex index.htm
WorkerLimit 10
InactivityTimeout 60

LoadModulePath "$LVSERVER_MODULEPATHS"
LoadModule LVAuth lvauthmodule
LoadModule LVRFP lvrfpmodule

#
# Pipeline Definition
#

SetConnector netConnector

AddHandler LVAuth
AddHandler LVRFP

AddHandler fileHandler ""

AddOutputFilter chunkFilter


</Property>
		<Property Name="target.WebServer.Enabled" Type="Bool">false</Property>
		<Property Name="target.WebServer.LogEnabled" Type="Bool">false</Property>
		<Property Name="target.WebServer.LogPath" Type="Path">/c/ni-rt/system/www/www.log</Property>
		<Property Name="target.WebServer.Port" Type="Int">80</Property>
		<Property Name="target.WebServer.RootPath" Type="Path">/c/ni-rt/system/www</Property>
		<Property Name="target.WebServer.TcpAccess" Type="Str">c+*</Property>
		<Property Name="target.WebServer.Timeout" Type="Int">60</Property>
		<Property Name="target.WebServer.ViAccess" Type="Str">+*</Property>
		<Property Name="target.webservices.SecurityAPIKey" Type="Str">PqVr/ifkAQh+lVrdPIykXlFvg12GhhQFR8H9cUhphgg=:pTe9HRlQuMfJxAG6QCGq7UvoUpJzAzWGKy5SbZ+roSU=</Property>
		<Property Name="target.webservices.ValidTimestampWindow" Type="Int">15</Property>
		<Item Name="CSM_Modules" Type="Folder" URL="../CSM_Modules">
			<Property Name="NI.DISK" Type="Bool">true</Property>
		</Item>
		<Item Name="Server" Type="Folder" URL="../Server">
			<Property Name="NI.DISK" Type="Bool">true</Property>
		</Item>
		<Item Name="Dependencies" Type="Dependencies">
			<Item Name="user.lib" Type="Folder">
				<Item Name="_csm_user_lib_dir.vi" Type="VI" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_scripts/_csm_user_lib_dir.vi"/>
				<Item Name="API String Arguments Support.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/addons/API-String-Arugments-Support/API String Arguments Support.lvlib"/>
				<Item Name="Build Error Cluster__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/error/error.llb/Build Error Cluster__ogtk.vi"/>
				<Item Name="Communicable State Machine.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/Communicable State Machine.lvlib"/>
				<Item Name="Convert File Extension (Path)__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/file/file.llb/Convert File Extension (Path)__ogtk.vi"/>
				<Item Name="Convert File Extension (String)__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/file/file.llb/Convert File Extension (String)__ogtk.vi"/>
				<Item Name="Convert File Extension__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/file/file.llb/Convert File Extension__ogtk.vi"/>
				<Item Name="CSM-Helper Utility.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/CSM-Helper Utility.lvlib"/>
				<Item Name="CSM-Helper.lvclass" Type="LVClass" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/CSM-Helper.lvclass"/>
				<Item Name="csmHelper-CaseStructure.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/CaseStructure/csmHelper-CaseStructure.lvlib"/>
				<Item Name="csmHelper-Diagram.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/Diagram/csmHelper-Diagram.lvlib"/>
				<Item Name="csmHelper-PairedTunnels.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/PairedTunnels/csmHelper-PairedTunnels.lvlib"/>
				<Item Name="csmHelper-Relationship.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/Relationship/csmHelper-Relationship.lvlib"/>
				<Item Name="csmHelper-SubVI.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/SubVI/csmHelper-SubVI.lvlib"/>
				<Item Name="csmHelper-Terminals.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/Utility/_Support/Terminals/csmHelper-Terminals.lvlib"/>
				<Item Name="INI-Static-Variable-Support.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/addons/INI-Static-Variable-Support/INI-Static-Variable-Support.lvlib"/>
				<Item Name="LabVIEW-Config.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/addons/INI-Static-Variable-Support/LabVIEW-Config/LabVIEW-Config.lvlib"/>
				<Item Name="lvcsm File Support.lvlib" Type="Library" URL="/&lt;userlib&gt;/_NEVSTOP/Communicable State Machine(CSM)/_csmHelper/lvcsmFile/lvcsm File Support.lvlib"/>
				<Item Name="Tick Count (ms)__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/time/time.llb/Tick Count (ms)__ogtk.vi"/>
				<Item Name="Wait (ms)__ogtk.vi" Type="VI" URL="/&lt;userlib&gt;/_OpenG.lib/time/time.llb/Wait (ms)__ogtk.vi"/>
			</Item>
			<Item Name="vi.lib" Type="Folder">
				<Item Name="8.6CompatibleGlobalVar.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/config.llb/8.6CompatibleGlobalVar.vi"/>
				<Item Name="Acquire Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Acquire Semaphore.vi"/>
				<Item Name="Add State(s) to Queue__JKI_lib_State_Machine.vi" Type="VI" URL="/&lt;vilib&gt;/addons/_JKI Toolkits/State Machine/_JKI_lib_State_Machine.llb/Add State(s) to Queue__JKI_lib_State_Machine.vi"/>
				<Item Name="AddNamedSemaphorePrefix.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/AddNamedSemaphorePrefix.vi"/>
				<Item Name="Application Directory.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/file.llb/Application Directory.vi"/>
				<Item Name="Build State String with Arguments__JKI_lib_State_Machine.vi" Type="VI" URL="/&lt;vilib&gt;/addons/_JKI Toolkits/State Machine/_JKI_lib_State_Machine.llb/Build State String with Arguments__JKI_lib_State_Machine.vi"/>
				<Item Name="BuildHelpPath.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/BuildHelpPath.vi"/>
				<Item Name="Check if File or Folder Exists.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/libraryn.llb/Check if File or Folder Exists.vi"/>
				<Item Name="Check Special Tags.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Check Special Tags.vi"/>
				<Item Name="Clear Errors.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Clear Errors.vi"/>
				<Item Name="Convert property node font to graphics font.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Convert property node font to graphics font.vi"/>
				<Item Name="Create Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Create Semaphore.vi"/>
				<Item Name="DateTime.ISO8601.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/DateTime.ISO8601.lvclass"/>
				<Item Name="DateTimeFormatter.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter/DateTimeFormatter.lvclass"/>
				<Item Name="Destroy Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Destroy Semaphore.vi"/>
				<Item Name="Details Display Dialog.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Details Display Dialog.vi"/>
				<Item Name="DialogType.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/DialogType.ctl"/>
				<Item Name="DialogTypeEnum.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/DialogTypeEnum.ctl"/>
				<Item Name="Epoch.GPS.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/epoch.GPS/Epoch.GPS.lvclass"/>
				<Item Name="Epoch.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/epoch/Epoch.lvclass"/>
				<Item Name="Epoch.UNIX.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/epoch.UNIX/Epoch.UNIX.lvclass"/>
				<Item Name="Epoch.UTC.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/epoch.UTC/Epoch.UTC.lvclass"/>
				<Item Name="EpochNotTimestamp.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/EpochNotTimestamp.vi"/>
				<Item Name="Epochs -- enum.ctl" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/Epochs -- enum.ctl"/>
				<Item Name="Error Cluster From Error Code.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Error Cluster From Error Code.vi"/>
				<Item Name="Error Code Database.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Error Code Database.vi"/>
				<Item Name="ErrWarn.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/ErrWarn.ctl"/>
				<Item Name="eventvkey.ctl" Type="VI" URL="/&lt;vilib&gt;/event_ctls.llb/eventvkey.ctl"/>
				<Item Name="Find Tag.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Find Tag.vi"/>
				<Item Name="Format Message String.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Format Message String.vi"/>
				<Item Name="General Error Handler Core CORE.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/General Error Handler Core CORE.vi"/>
				<Item Name="General Error Handler.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/General Error Handler.vi"/>
				<Item Name="Get Local UTC Offset.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/Get Local UTC Offset.vi"/>
				<Item Name="Get Semaphore Status.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Get Semaphore Status.vi"/>
				<Item Name="Get String Text Bounds.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Get String Text Bounds.vi"/>
				<Item Name="Get Text Rect.vi" Type="VI" URL="/&lt;vilib&gt;/picture/picture.llb/Get Text Rect.vi"/>
				<Item Name="GetHelpDir.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/GetHelpDir.vi"/>
				<Item Name="GetNamedSemaphorePrefix.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/GetNamedSemaphorePrefix.vi"/>
				<Item Name="GetRTHostConnectedProp.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/GetRTHostConnectedProp.vi"/>
				<Item Name="GregorianCal_MonthLengths.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/GregorianCal_MonthLengths.vi"/>
				<Item Name="Is Path and Not Empty.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/file.llb/Is Path and Not Empty.vi"/>
				<Item Name="isDST.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/isDST.vi"/>
				<Item Name="isLeapYear (integer).vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/isLeapYear (integer).vi"/>
				<Item Name="isLeapYear (timestamp).vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/isLeapYear (timestamp).vi"/>
				<Item Name="ISO Day -- enum.ctl" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/ISO Day -- enum.ctl"/>
				<Item Name="ISO8601 Date Formats.ctl" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/ISO8601 Date Formats.ctl"/>
				<Item Name="ISO8601 DateString to Timestamp.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/ISO8601 DateString to Timestamp.vi"/>
				<Item Name="JKI TCP Server.lvclass" Type="LVClass" URL="/&lt;vilib&gt;/addons/_JKI.lib/_JKI_lib_TCP_Server.llb/JKI TCP Server.lvclass"/>
				<Item Name="Longest Line Length in Pixels.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Longest Line Length in Pixels.vi"/>
				<Item Name="LVBoundsTypeDef.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVBoundsTypeDef.ctl"/>
				<Item Name="LVDateTimeRec.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVDateTimeRec.ctl"/>
				<Item Name="LVPointTypeDef.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVPointTypeDef.ctl"/>
				<Item Name="LVPositionTypeDef.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVPositionTypeDef.ctl"/>
				<Item Name="LVRectTypeDef.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/miscctls.llb/LVRectTypeDef.ctl"/>
				<Item Name="NI_Data Type.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/Data Type/NI_Data Type.lvlib"/>
				<Item Name="NI_FileType.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/lvfile.llb/NI_FileType.lvlib"/>
				<Item Name="NI_LVConfig.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/config.llb/NI_LVConfig.lvlib"/>
				<Item Name="NI_PackedLibraryUtility.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/LVLibp/NI_PackedLibraryUtility.lvlib"/>
				<Item Name="Not A Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Not A Semaphore.vi"/>
				<Item Name="Not Found Dialog.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Not Found Dialog.vi"/>
				<Item Name="Obtain Semaphore Reference.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Obtain Semaphore Reference.vi"/>
				<Item Name="Parse State Queue__JKI_lib_State_Machine.vi" Type="VI" URL="/&lt;vilib&gt;/addons/_JKI Toolkits/State Machine/_JKI_lib_State_Machine.llb/Parse State Queue__JKI_lib_State_Machine.vi"/>
				<Item Name="Release Semaphore Reference.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Release Semaphore Reference.vi"/>
				<Item Name="Release Semaphore.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Release Semaphore.vi"/>
				<Item Name="Release Semaphore_71.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Release Semaphore_71.vi"/>
				<Item Name="RemoveNamedSemaphorePrefix.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/RemoveNamedSemaphorePrefix.vi"/>
				<Item Name="Search and Replace Pattern.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Search and Replace Pattern.vi"/>
				<Item Name="Semaphore Name &amp; Ref DB Action.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Semaphore Name &amp; Ref DB Action.ctl"/>
				<Item Name="Semaphore Name &amp; Ref DB.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Semaphore Name &amp; Ref DB.vi"/>
				<Item Name="Semaphore RefNum" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Semaphore RefNum"/>
				<Item Name="Semaphore Refnum Core.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Semaphore Refnum Core.ctl"/>
				<Item Name="Set Bold Text.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Set Bold Text.vi"/>
				<Item Name="Set String Value.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Set String Value.vi"/>
				<Item Name="Space Constant.vi" Type="VI" URL="/&lt;vilib&gt;/dlg_ctls.llb/Space Constant.vi"/>
				<Item Name="Synchronize Data Flow.vim" Type="VI" URL="/&lt;vilib&gt;/Utility/Synchronize Data Flow.vim"/>
				<Item Name="TagReturnType.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/TagReturnType.ctl"/>
				<Item Name="Three Button Dialog CORE.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Three Button Dialog CORE.vi"/>
				<Item Name="Three Button Dialog.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Three Button Dialog.vi"/>
				<Item Name="Timestamp to ISO8601 Date.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 Date.vi"/>
				<Item Name="Timestamp to ISO8601 Day.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 Day.vi"/>
				<Item Name="Timestamp to ISO8601 UTC DateTime.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 UTC DateTime.vi"/>
				<Item Name="Timestamp to ISO8601 Week.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 Week.vi"/>
				<Item Name="Timestamp to ISO8601 Year.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/formatter.iso8601/Timestamp to ISO8601 Year.vi"/>
				<Item Name="TRef Traverse.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/traverseref.llb/TRef Traverse.vi"/>
				<Item Name="TRef TravTarget.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/traverseref.llb/TRef TravTarget.ctl"/>
				<Item Name="Trim Whitespace.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/Trim Whitespace.vi"/>
				<Item Name="UTC Offset to Seconds.vi" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/UTC Offset to Seconds.vi"/>
				<Item Name="UTC Offsets -- enum.ctl" Type="VI" URL="/&lt;vilib&gt;/LabVIEW Open Source Project/Epoch Date &amp; Time/UTC Offsets -- enum.ctl"/>
				<Item Name="Validate Semaphore Size.vi" Type="VI" URL="/&lt;vilib&gt;/Utility/semaphor.llb/Validate Semaphore Size.vi"/>
				<Item Name="VI Scripting - Traverse.lvlib" Type="Library" URL="/&lt;vilib&gt;/Utility/traverseref.llb/VI Scripting - Traverse.lvlib"/>
				<Item Name="whitespace.ctl" Type="VI" URL="/&lt;vilib&gt;/Utility/error.llb/whitespace.ctl"/>
			</Item>
		</Item>
		<Item Name="Build Specifications" Type="Build"/>
	</Item>
</Project>

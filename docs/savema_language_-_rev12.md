Savema Printer Programming Language (Rev.12)   03/08/2022




                 SPPL
SAVEMA THERMAL TRANSFER
 PROGRAMMING LANGUAGE
                (Revision-12)




Markalama ve Kodlama Makinaları San.Tic. Ltd. Sti.
 Fevzi Cakmak Mah. Ahmet Petekci Cad. No :5L/1
             Karatay / KONYA / TURKIYE
                Tel : +90 332 239 2339
                Fax : +90 332 239 2319
             Web: www.savema.com.tr

  Document Version : Rev.11 © Copyright 2022 by SAVEMA TTO




                          ~1~
                         Savema Printer Programming Language (Rev.12)     03/08/2022


Revision Notes
The following changes have been made to this document.

1- SPLAMQ(Append Multi Queue Datas) command is added. That command provides to
    add dynamic data to more than one queue.
2- SPLGMQ(Get Multi Queue Capacity) command is added. That command returns item
    count of specified queues.
3- SPLCMQ(Clear Multi Queue Datas) command is added. That command deletes data
    in specified queues.
4- SPMCLV (Change Logo Value) command is added. That command provides to change
    logo remotely.
5- SPLSLJ (Select&Load Job) command is added. That command provides to load job.
6- SPLLJV(Load Job Values) command is added. That command provides to loa dd to
    job and change variables.
  6- SPLKTD (send to template data to storage ) command is added. That command
    provides to send template data printer storage but dont load. ( SPLTDS can send
                                  template data to storage and load in same time )
7- SPLRTF{Template name} -- Receive Template File from printer memory to pc
    memory
8- SPLRDF{CSV file name} -- Receive Data File from printer memory to pc memory



Note: Controller software version must be minimum v3.18 to use above changes.




                                   ~2~
                                    Savema Printer Programming Language (Rev.12)                             03/08/2022




                                             CONTENTS
1.      General Rules for SPPL .........................................6
     1.1.    Command Structure ....................................................................................... 6
     1.2.    Command List ................................................................................................ 6

2.      Configuration Commands ................................. 12
     2.1.    Set/Get System Date&Time and Time Offset................................................. 12
     2.2.    Set/Get Network Configuration .................................................................... 13
     2.3.    Set Get RS-232 Configuration ........................................................................ 14
     2.4.    Set/Get Print Speed(Intermitttent) .............................................................. 15
     2.5.    Set/Get Print Delay Value ............................................................................. 16
     2.6.    Set/Get Darkness(Contrast) Value..................................................................17
     2.7.    Set/Get Print Rotation .................................................................................. 17
     2.8.    Set/Get Horizontal Position .......................................................................... 18
     2.9.    Set/Get Vertical Position ................................................................................19
     2.10.   Set/Get Mirroring Option.............................................................................. 19
     2.11.   Set/Get RibbonSave Mode ............................................................................ 20
     2.12.   Set/Get Internal Contact Mode(Continuous only) ......................................... 21
     2.13.   Set/Get Trigger Contact Mode(Continuous only) .......................................... 22
     2.14.   Set/Get All Settings ....................................................................................... 23
     2.15.   Set/Get System Parameters .......................................................................... 25
     2.16.   Set/Get All System Parameters ..................................................................... 26
     2.17.   Set/Get One Additional Settings.................................................................... 27
     2.18.   Set/Get All Additional Settings ...................................................................... 27
     2.19.   Set /Get System Language ............................................................................ 28
     2.20.   Set/Get Administrator Password ................................................................... 30
     2.21.   Return to Factory Settings............................................................................. 30
     2.22.   Set/Get Print Request Message .................................................................... 31



                                                   ~3~
                                          Savema Printer Programming Language (Rev.12)                               03/08/2022

3.      Label Designing Commands .............................. 32
     3.1.       Create Template Datas and Template Structure ............................................ 32
        3.1.1. General Template Datas ............................................................................... 33
        3.1.2. General Object Datas .................................................................................... 35
        3.1.3. Objects(Content) .......................................................................................... 35
            3.1.3.1. Date .................................................................................................. 35
            3.1.3.2. Time .................................................................................................. 38
            3.1.3.3. Text ................................................................................................... 40
            3.1.3.4. RichText ............................................................................................ 41
            3.1.3.5. Counter ............................................................................................. 42
            3.1.3.6. Logo(Image) ...................................................................................... 45
            3.1.3.7. Shape ................................................................................................ 46
            3.1.3.8. Shift Code .......................................................................................... 47
            3.1.3.9. Barcode ..............................................................................................49
            3.1.3.10. 2D Barcode ........................................................................................ 56
            3.1.3.11. Database…......................................................................................... 65
            3.1.3.12. Table… ............................................................................................... 66
        3.1.4. Font .............................................................................................................. 68
     3.2.         Load Template File from Printer .................................................................... 70
     3.3.         Get Active Template ..................................................................................... 70
     3.4.         Get Stored Templates ................................................................................... 70
     3.5.         Create Database File ..................................................................................... 71
     3.6.         Get Stored Data Files .................................................................................... 72
     3.7.         Delete Template File ...................................................................................... 72
     3.8.         Delete All Templates ...................................................................................... 72
     3.9.         Delete Data File ............................................................................................ 73
     3.10.        Delete All Data Files ...................................................................................... 73
     3.11.        Clear Data Buffer ........................................................................................... 74
     3.12.        Load Font File… ............................................................................................. 74
     3.13.        Get Font Files .................................................................................................75
     3.14.        Delete Font File. ............................................................................................ 75
     3.15.        Get Field Names.............................................................................................75
     3.16.        Get Field Value… ........................................................................................... 76
     3.17.        Append Queue Datas .................................................................................... 77
     3.18.        Append Multi Queue Datas ............................................................................ 77
     3.19.        Get Queue Capacity ...................................................................................... 78
     3.20.        Get Multi Queue Capacity ............................................................................. 79
     3.21.        Clear Queue Datas .........................................................................................79



                                                         ~4~
                                    Savema Printer Programming Language (Rev.12)                                 03/08/2022

     3.22.   Clear Multi Queue Datas ............................................................................... 80

4.      Modification Commands ....................................81
     4.1.    Changing Text Value ..................................................................................... 81
     4.2.    Changing Barcode Value ............................................................................... 82
     4.3.    Changing 2D Barcode Value .......................................................................... 82
     4.4.    Changing Counter Value ............................................................................... 83
     4.5.    Changing Logo .............................................................................................. 83
     4.6.    Changing Selected Values ............................................................................. 84

5.      Print Commands ............................................... 86
     5.1.    Start Print ..................................................................................................... 86
     5.2.    Set/Get Print Count for Limited print .............................................................86
     5.3.    Stop Print .......................................................................................................87
     5.4.    One Test Print ................................................................................................87
     5.5.    Status of Printer ............................................................................................ 88

6.      General Commands............................................ 89
     6.1.    Send User Message to Printer ....................................................................... 89
     6.2.    General Response Command From Printer ................................................... 89
     6.3.    Get Total Print Count .....................................................................................90
     6.4.    Get Current Print Count ................................................................................ 90
     6.5.    Get Firmware Version .................................................................................... 90
     6.6.    Get Remaining Ribbon(From Cassette models) ............................................. 91
     6.7.    Get Serial Number of Printer ......................................................................... 91
     6.8.    Set Lock Interface ......................................................................................... 91
     6.9.    Get Lock Interface ..........................................................................................92

7.      Traverse Commands .......................................... 93
     7.1.    Set/Get Pack Size ......................................................................................... 93
     7.2.    Set/Get Print Count ...................................................................................... 94
     7.3.    Set/Get Print Position ................................................................................... 94
     7.4.    Set/Get Pack Distance… ................................................................................. 95
     7.5.    Set/Get Printing Area .................................................................................... 96
     7.6.    Set/Get All Traverse Parameters ................................................................... 96

8.      System Parameters Explanation.........................98
     8.1.    32x40I, 32x50I, 53x40I and 53x50I Parameters ............................................. 98
     8.2.    32x70I, 53x70I, 53x125I, 107x75I and 107x125I Parameters ....................... 100

     8.3.    32C Parameters .......................................................................................... 102

                                                    ~5~
                                    Savema Printer Programming Language (Rev.12)                          03/08/2022

     8.4.      32C with Cassette, 53C and 107C Paremeters Explanation .......................... 104
     8.5.      32TR, 53TR and 107TR Parameters Explanation ........................................... 106

9.      Warnings ........................................................ 108
     9.1.      Command Limitations ................................................................................. 109
     9.2.      Command Operating Conditions ................................................................. 110


1. General Rules for SPPL
      SPPL is programming language for controlling Savema Thermal Transfer printer over
Ethernet/RS-232 communication.

1.1     SPPL Command Structure
        SPPL commands have some rules which is shown in below;
        - SPPL commands starts with ~ character and ends with ^ character.(ie ~SPPSAP^)
        - SPPL commands seperates with | character for more than one commands.        (ie
           ~SPPSLQ{1000}| SPPSAP^). This character only have to be used for seperate
           commands.
        - SPPL command parameters defines between { and } characters.
        - (ie. ~ SPPSLQ {1000}^)
        - Set and Change Commands parameters seperates with > character. (ie.
           ~SPCSSC{115200>None>8>1}^)
           Get Commands parameters seperates with < characters. (ie.
           ~ SPGRES{ SPCGSC:115200<None<8<1}^)
           Modification commands and Create Data File command parameters seperates
           with ~gt~ text. (ie. ~SPMCSV{text1~gt~Savema}^
        - SPPL command letters are created according to some rules.
            o SP means Savema Printer
            o 3rd character indicates Command type. Command types with letters are;
                      ▪ C : Configuration Commands
                      ▪ L : Label Commands
                      ▪ M : Modification Commands
                      ▪ P : Print Commands
                      ▪ G : General Commands
            o Last 3 characters are abbreviation of Command Name

            Forexample; SPCSNC command letters are separating like below;

            o SP is Savema Printer
            o C is Configuration Command type
            o SNC is Set Network Configuration



                                                   ~6~
                                           Savema Printer Programming Language (Rev.12)            03/08/2022



1.2      Command List
         SPPL commands seperates according to using type. There are 5 types of command
         groups in SPPL.
         - Configuration Commands
         - Label Designing Commands
         - Modification Commands
         - Print Commands
         - General Commands
         - Traverse Commands

         Note : Some commands doesn’t supported by all printers. If so, printer sends FAIL
         message when getting incompatible command. Please see part 9.1) Command
         Limitations .
         Note : Data transfer time changes according to communication type and speed. If
         data is big, transfer time increases.
         All of commands are listed in below as a table format

      SAVEMA PRINTER PROGRAMMING LANGUAGE COMMAND LIST
Command    Explanation             Usage                                  Example
                                       CONFIGURATION COMMANDS
           Set System
SPCSDT     Date&Time                ~SPCSDT{DD>MM>YYYY>HH>mm>SS>OO}^ ~SPCSDT{25>01>2015>11>36>00>00}^
           and Time Offset
           Get System
SPCGDT     Date&Time                ~SPCGDT^                              ~SPCGDT^
           and Time Offset
           Set Network              ~SPCSNC{IP Address>Subnet             ~SPCSNC{192.168.1.123>
SPCSNC     Configuration            Mask>Gateway>Port number}^            255.255.255.0>192.168.1.1>9100}^
           Get Network
SPCGNC     Configuration            ~SPCGNC^                              ~SPCGNC^
           Set RS-232               ~SPCSSC{Baud Rate> Parity>
SPCSSC     Configuration
                                                                          ~SPCSSC{115200>None>8>1}^
                                    Data Bits> Stop Bits}^
           Get RS-232
SPCGSC     Configuration
                                    ~SPCGSC^                              ~SPCGSC^
           Set Print Speed
SPCSPS                              ~SPCSPS{Print Speed}^                 ~SPCSPS{200}^
           (Intermitttent)
           Get Print Speed
SPCGPS     (Intermitttent)
                                    ~SPCGPS^                              ~SPCGPS^

SPCSPD     Set Print Delay value    ~SPCSPD{Print Delay}^                 ~SPCSPD{10}^
SPCGPD     Get Print Delay value    ~SPCGPD^                              ~SPCGPD^
           Set
SPCSDV     Darkness(Contrast)       ~SPCSDV{Contrast}^                    ~SPCSDV{100}^
           Value
           Get
SPCGDV     Darkness(Contrast)       ~SPCGDV^                              ~SPCGDV^
           Value
SPCSPR     Set Print Rotation       ~SPCSPR{Print Rotation}^              ~SPCSPR{180}^
SPCGPR     Get Print Rotation       ~SPCGPR^                              ~SPCGPR^
           Set Horizontal
SPCSHP     Position                 ~SPCSHP{Horizontal Position Value}^   ~SPCSHP{0}^


                                                         ~7~
                                       Savema Printer Programming Language (Rev.12)             03/08/2022

         Get Horizontal
SPCGHP   Position
                                 ~SPCGHP^                                ~SPCGHP^
SPCSVP   Set Vertical Position   Will be used in then future
SPCGVP   Get Vertical Position   Will be used in then future
SPCSMO   Set Mirroring Option    ~SPCSMO{Mirroring Option}^              ~SPCSMO{0}^
SPCGMO   Get Mirroring Option    ~SPCGMO^                                ~SPCGMO^
         Set RibbonSave          ~SPCSRS{Direction>Column No>Shifting
SPCSRS   Mode
                                                                         ~SPCSRS{0>2>4}^
                                 length }^
         Get RibbonSave
SPCGRS   Mode
                                 ~SPCGRS^                                ~SPCGRS^
         Set Internal Contact
                                 ~SPCSIC{Internal Contact Mode State>
SPCSIC   Mode                                                            ~SPCSIC{1>100}^
         (Continuous only)       Package length }^
         Get Internal Contact
SPCGIC   Mode                    ~SPCGIC^                                ~SPCGIC^
         (Continuous only)
         Set Trigger Contact
                                 ~SPCSTC{Trigger Contact Mode State>
SPCSTC   Mode                                                            ~SPCSTC{1>3>100}^
         (Continuous only)       Print Count>Package length }^
         Get Trigger Contact
SPCGTC   Mode                    ~SPCGTC^                                ~SPCGTC^
         (Continuous only)
                                 ~SPCSAS{Print Speed>Print Delay>Darkness
                                 Value>RibbonSave Mode Direction>
                                 RibbonSave Mode Column No> RibbonSave
                                 Mode Package Length>Internal Contact
                                                                          ~SPCSAS{300>2>100>0>1>0>0>30>1>3>60
SPCSAS   Set All Settings        Mode State>Internal Contact Mode
                                                                          }^
                                 Package Length>Trigger Contact Mode
                                 State>Trigger Contact Mode Print Count
                                 >Trigger Contact Mode Package Length}^

SPCGAS   Get All Settings        ~SPCGAS^                                ~SPCGAS^
         Set System
SPCSSP                           ~SPCSSP{Parameter No> Parameter value}^ ~SPCSSP{1>25}^
         Parameter
         Get System
SPCGSP   Parameter               ~SPCGSP{Parameter No }^                 ~SPCGSP{1}^
                                 ~SPCSPA{P1>P2>P3,P4>P5>P6>P7>P8>P9>P
         Set All System          10>P11>P12>P13>P14>P15>P16>P17>P18> ~SPCSPA{25>27>300>200>31>77>0>24>25
SPCSPA   Parameters              P19>P20}^                            >0>12>65>0>5>0>23>0>4>0>0>400}^
                                 P means Parameter
         Get All System
SPCGPA   Parameters              ~SPCGPA^                                ~SPCGPA^
SPCSSL   Set System Language     ~SPCSSL{System Languge Code }^          ~SPCSSL{02}^
SPCGSL   Get System Language     ~SPCGSL^                                ~SPCGSL^
         Set Administrator
SPCSAP   Password
                                 ~SPCSAP{System Password}^               ~SPCSAP{123456}^
         Get Administrator
SPCGAP   Password
                                 ~SPCGAP^                                ~SPCGAP^
         Return to Factory
SPCSFS   Settings
                                 ~SPCSFS^                                ~SPCSFS^
         Set Print Request       ~SPCSPM{Print Request Active|Passive>
SPCSPM                                                                   ~SPCSPM{0>OK}^
         Message                 Print Message}^
         Get Print Request
SPCGPM   Message                 ~SPCGPM^                                ~SPCGPM^

LABEL DESIGNING COMMANDS




                                                     ~8~
                                     Savema Printer Programming Language (Rev.12)          03/08/2022

                                                                    ~SPLTDS{<Template>
                                                                  <General>
                                                                  <MachineType>53x70I</MachineType>
                                                                  <Name>temp1_53.rox</Name>
                                                                  <Width>640</Width>
                                                                  <Height>480</Height>
                                                                  </General>
                                                                  <Object>
                                                                  <ObjectType>Text</ObjectType>
                                                                  <Name>text1</Name>
                                                                  <X>10</X>
                                                                  <Y>63</Y>
         Create Template
                                                                  <W>105</W>
         Datas
SPLTDS                       ~SPLTDS{Template Datas}^             <H>33</H>
         and Template
         Structure                                                <Rotate>180</Rotate>
                                                                  <Content>
                                                                  <Data>savema Printer</Data>
                                                                  <Source>Internal</Source>
                                                                  <MagnificationRatio>100</
                                                                  MagnificationRatio>
                                                                  </Content>
                                                                  <Font>
                                                                  <Name>Arial</Name>
                                                                  <Size>15</Size>
                                                                  <Style>Bold,Italic</Style>
                                                                  </Font></Object>
                                                                  </Template>}^
                                                                    ~ SPLKTD {<Template>
                                                                  <General>
                                                                  <MachineType>53x70I</MachineType>
                                                                  <Name>temp1_53.rox</Name>
                                                                  <Width>640</Width>
                                                                  <Height>480</Height>
                                                                  </General>
                                                                  <Object>
                                                                  <ObjectType>Text</ObjectType>
                                                                  <Name>text1</Name>
                                                                  <X>10</X>
                                                                  <Y>63</Y>
         Keep Template                                            <W>105</W>
SPLKTD   Data and Template   ~ SPLKTD {Template Datas}^           <H>33</H>
         Structure                                                <Rotate>180</Rotate>
                                                                  <Content>
                                                                  <Data>savema Printer</Data>
                                                                  <Source>Internal</Source>
                                                                  <MagnificationRatio>100</
                                                                  MagnificationRatio>
                                                                  </Content>
                                                                  <Font>
                                                                  <Name>Arial</Name>
                                                                  <Size>15</Size>
                                                                  <Style>Bold,Italic</Style>
                                                                  </Font></Object>
                                                                  </Template>}^
         Load Template
SPLLTF   from Printer
                             ~SPLLTF{Template File Name}^         ~SPLLTF{temp1_53.rox}^
         Receive CSV
SPLRDF   Data                ~SPLRDF{CSV file name}^              ~SPLRDF{database.csv }^
         Receive
SPLRTF   Template Data       ~SPLRTF{Template name}^              ~SPLRTF{Template name}^
         Get Active
SPLGAT   Template
                             ~SPLGAT^                             ~SPLGAT^
         Get Stored
SPLGST                       ~SPLGST^                             ~SPLGST^
         Templates

                                                 ~9~
                                 Savema Printer Programming Language (Rev.12)   03/08/2022
         Get Stored Data
SPLGSD   Files
                           ~SPLGSD^                           ~SPLGSD^




                                           ~ 10 ~
                                        Savema Printer Programming Language (Rev.12)                   03/08/2022

                                                                           ~SPLCDF{sample.csv~gt~abc
SPLCDF   Create Data File       ~SPLCDF{Data File Name~gt~File Content}^   bce
                                                                           cde}^
SPLDTF   Delete Template        ~SPLDTF{Template File Name}^               ~SPLDTF{temp1_53.rox}^
         Delete All
SPLDTA   Template               ~SPLDTA^                                   ~SPLDTA^
SPLDDF   Delete Data File       ~SPLDDF{Data File Name}^                   ~SPLDDF{datafile1.csv}^
SPLDDA   Delete All Data File   ~SPLDDA^                                   ~SPLDDA^
SPLCDB   Clear Data Buffer      ~SPLCDB^                                   ~SPLCDB^
SPLGFN   Get Field Names        ~SPLGFN{Template File Name}^               ~SPLGFN{temp1_53.rox}^

SPLGFV   Get Field Value        ~SPLGFN{ Field Name}^                      ~SPLGFN{BatchNo}^
         Append Queue                                                      ~SPLAQD{TextCSV~gt~AB001
SPLAQD                          ~SPLAQD{Field Name~gt~Datas}^
         Datas                                                             AB002}^
                                                                           ~SPLAMQ{PRDNAME~gt~PR01
                                ~SPLAMQ{Field Name1~gt~Datas1~gt~ Field    PR02~
         Append Multi
SPLAMQ                          Name2~gt~Datas2~gt~ Field                  PR03~gt~BATCH NO~gt~A01B
         Queue Datas
                                Name3~gt~Datas3~gt~…..}^                   A02B
                                                                           A03B}^
         Get Queue
SPLGQC   Capacity               ~SPLGQC{Field Name}^                       ~SPLGQC{TextCSV}^
         Get Multi Queue        ~SPLGMQ{Field Name1~gt~Field
SPLGMQ                                                                     ~SPLGMQ{PRDNAME~gt~ BATCH NO}^
         Capacity               Name2~gt~….}^
SPLCQD   Clear Queue Datas      ~SPLCQD{Field Name}^                       ~SPLCQD{TextCSV}^
         Clear Multi Queue      ~SPLCMQ{ Field Name1~gt~Field
SPLCMQ                                                                     ~SPLCMQ{PRDNAME~gt~ BATCH NO}^
         Datas                  Name2~gt~….}^
                                ~SPLSLJ{Template Name>Count>Template       ~SPLSLJ{t1_53.rox>1>t2_53.rox>2>t3_53.r
SPLSLJ   Select&Load Job
                                Name>Count>. .. }^                         ox>3>t4_53.rox>4>t5_53.rox>5}^
                                                                           ~SPLLJV{T1L_53.rox>2>AD~gt~sumak~gt~S
SPLLJV   Load Job Values        SPLLJV{Template Name>Count>Variables}
                                                                           KT~gt~19.07.2023}^
MODIFICATION COMMANDS
         Changing Text
SPMCTV                          ~SPMCTV{Name of Object~gt~Text Value}^     ~SPMCTV{brand_txt~gt~SAVEMA}^
         Value
         Changing Barcode       ~SPMCBV{Name of Object ~gt~Barcode         ~SPMCBV{barcodeno~gt~8691234567890}
SPMCBV   Value                  Value}^                                    ^
         Changing 2D
                                ~SPMC2D{ Name of Object ~gt~Barcode
SPMC2D   Barcode                                                           ~SPMC2D{qrcodeno~gt~savema12345}^
         Value                  Value}^
         Changing Counter       ~SPMCCV{Name of Object ~gt~Counter
SPMCCV   Value
                                                                           ~SPMCCV{counter1~gt~000055}^
                                Value}^
         Changing Logo
SPMCLV                          ~SPMCLV{Name of Object ~gt~Base64 data}^   ~SPMCLV{productlogo~gt~/9j/4….Q==}^
         Value
         Changing Selected      ~SPMCSV{Name of Object~gt~Text             ~SPMCSV{ brand_txt~gt~SAVEMA~gt~
SPMCSV   Values                 Value~gt~Name of Object~gt~Text Value}^    qrcodeno~gt~savema12345}^
PRINT COMMANDS
         Start Automatically
SPPSAP   Print
                                ~SPPSAP^                                   ~SPPSAP^
         Set Print Count for
SPPSLQ   Limited print
                                ~SPPSLQ{Print Quantity}^                   ~SPPSLQ{1000}^
         Get Print Count for
SPPGLQ                          ~SPCGLQ^                                   ~SPPGLQ^
         Limited print
SPPSTP   Stop Print             ~SPPSTP^                                   ~SPPSTP^
SPPOTP   One Test Print         ~SPPOTP^                                   ~SPPOTP^
SPPSTA   Status of Printer      ~SPPSTA^                                   ~SPPSTA^
GENERAL COMMANDS

                                                     ~ 11 ~
                                      Savema Printer Programming Language (Rev.12)                 03/08/2022

         Send User
                                                                       ~SPGSUM{Package finished.
SPGSUM   Message to           ~SPGSUM{User Message}^
         Printer                                                       Please stop printer}^
         General Response
SPGRES   Command From         ~SPGRES{Response}^                       ~SPGRES{950225}^
         Printer
         Get Total Print
SPGGTP   Count                ~SPGGTP^                                 ~SPGGTP^
         Get Firmware
SPGGFW   Version
                              ~SPGGFV^                                 ~SPGGFV^
         Get Remaining
SPGGRR   Ribbon(From          ~SPGGRR^                                 ~SPGGRR^
         Cassette models)
         Get Serial Number
SPGGSN   of Printer
                              ~SPGGSN^                                 ~SPGGSN^

         Get Current Print
SPGGCP   Count
                              ~SPGGCP^                                 ~SPGGCP^

SPGSLI   Set Lock Interface   ~SPGSLI{Lock/Unlock}^                    ~SPGSLI{1}^
SPGGLI   Get Lock Interface   ~SPGGLI^                                 ~SPGGLI^
TRAVERSE COMMANDS
SPTSPS   Set Pack Size        ~SPTSPS{Pack Size(mm)}^                  ~SPTSPS{60}^
SPTGPS   Get Pack Size        ~ SPTGPS^                                ~SPTGPS^
SPTSPC   Set Print Count      ~ SPTSPC{Print Count}^                   ~SPTSPC{5}^
SPTGPC   Get Print Count      ~SPTGPC^                                 ~SPTGPC^
SPTSPP   Set Print Position   ~SPTSPP{Print position(mm)}^             ~SPTSPP{10}^
SPTGPP   Get Print osition    ~SPTGPP^                                 ~SPTGPP^
         Set Pack Distance
SPTSPD   From Beginning       ~SPTSPD{Pack distance(mm)}^              ~SPTSPD{50}^
         Get Pack istance
SPTGPD   From Beginning       ~ SPTGPD^                                ~SPTGPD^
SPTSPA   Set Printing Area    ~ SPTSPA{Printing Area}^                 ~SPTSPA{400}^
SPTGPA   Get Printing Area    ~SPTGPA^                                 ~SPTGPA^
         Set All Traverse     ~SPTSTP{Pack Size>Print Count>Print
SPTSTP   Parameters
                                                                       ~SPTSTP{60>5>10>50>400}^
                              Position>Pack Distance>Printing Area}^
         Get All Traverse
SPTGTP   Parameters
                              ~SPTGTP^                                 ~SPTGTP^

                                            Table-1) Command List




                                                   ~ 12 ~
                                 Savema Printer Programming Language (Rev.12)        03/08/2022


2. Configuration Commands
        Configuration Commands allows to make changing on printer settings. Some settings
affects printer working. So, must be carefull using this commands

2.1      Set/Get System Date&Time and Time Offset
      SPCSDT :Allows to adjust system date and time and Time Offset.
       Time offset is using for changing template date before or after midnight. This is changing
      between -12(before midnight) and 12(after midnight).
      Date and time value and time offset value must be sent as a parameter with this
      command.
      Printer sends OK message when setting date&time is successed or sends FAIL message
      when setting date&time is failed.

      Using         ~SPCSDT{DD>MM>YYYY>HH>mm>SS>OO}^

                 Parameters;
                    DD: Day (2 digits). Day Value must be between 00-31. February has 28-29
                    days and some months has 30 days. So, be carefull when setting the value
                    of the day.
                    MM : Month (2 digits ). Month Value must be between 00-12.
                    YYYY: Year (4 digits). Year value must be between 1900-3000.
                    HH :Hour(2 digits). Hour value is adjusted according to 24 Hours. So, hour
                    value must be between 00-23.
                    mm : Minute(2 digits). Minute value must be between 00-59.
                    SS : Second(2 digits). Second value must be between 00-59.
                    OO : Time Offset(2 digits). Time offset value must be between -12 and 12.

      Example       ~SPCSDT{25>07>2017>11>36>00>00}^ – with timeoffset

                    ~SPCSDT{25>07>2017>11>36>00>02}^ – without timeoffset


                    Return Value(On Successed) :
                    ~ SPGRES{ SPCSDT:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{ SPCSDT:FAIL}^




                                            ~ 13 ~
                               Savema Printer Programming Language (Rev.12)      03/08/2022

         SPCGDT : Returns system date&time and time offset value from printer. İf any
         problem happens while commands processing, printer sends FAIL message.

      Using        ~SPCGDT^

      Example      ~SPCGDT^

                   Return Value(On Successed) :
                   ~ SPGRES{ SPCGDT :25<07<2017<11<36<00<00}^




2.2      Set/Get Network Configuration
         SPCSNC : Allows to configure network parameters of printer. This parameters adjust
         via Ethernet communication, existing communication will be disconnected after
         finish adjustment and needs to connect with new parameters(IP address …etc). If RS-
         232 is used for configure, there is no disconnection problem after adjust network
         parameters. SPCSNC commands is sending with some parameters for configuration.
         Printer sends OK message when setting network configuration is successed or sends
         FAIL message when setting network configuration is failed.


      Using        ~SPCSNC{IP Address>Subnet Mask>Gateway>Port number}^

                Parameters;
                   IP Address: Printer IP(Internet Protocol) Address must be IPv4 standart
                   This IP address must be unique in network. Otherwise devices(have same
                   IP address) can be conflict while working in same network.
                   Subnet Mask :Subnet Mask must be adjust according to IP Address class.
                   Gateway:This addressis same one network and all devices(same network)
                   uses same gateway address. Gateway address have to be same for
                   communicate with printer.
                   Port Number : This is 9100 in printers. So, must be use 9100 as a port
                   number.

                   Return Value(On Successed) :
                   ~ SPGRES{ SPCSNC:OK}^
                   Return Value(On Failed) :
                   ~ SPGRES{ SPCSNC:FAIL}^

      Example      ~SPCSNC{192.168.1.123>255.255.255.0>192.168.1.1>9100}^

                   ~SPCSNC{192.168.1.100>255.255.255.0>192.168.1.1>9100}^




                                          ~ 14 ~
                                Savema Printer Programming Language (Rev.12)      03/08/2022

         Note : Network parameters(Subnet Mask, Gateway Address) can be learn from
         Command Prompt with ipconfig command like below image.




         SPCGNC : Returns network parameters.

      Using         ~SPCGNC^

      Example       ~SPCGNC^

                    Return Value(On Successed) :
                    ~ SPGRES{ SPCGNC:192.168.1.123<255.255.255.0<192.168.1.1<9100}^



2.3      Set/Get RS-232(Serial) Configuration
         SPCSSC : Allows to set RS-232 parameters in printer. Please use 9 pins(DB9) standart
         crossover cable for communicate with printer via RS-232. RS -232 is configuring via
         RS-232 or Ethernet. İf use RS-232 for configuration, new parameters must be apply to
         device which will comunicate with printer.
         Printer sends OK message when setting RS-232 configuration is successed or sends
         FAIL message when setting RS-232 configuration is failed.


      Using         ~SPCSSC{Baud Rate>Parity>Data Bits>Stop Bits}^

                Parameters;
                   Baud Rate:Adjusts data flowing speed as bits per second. İt must be
                   happen between 1200 – 115200bps. Can be 1200-2400-4800-9600-14400-
                   19200-28800-38400-56000-57600-115200. Printer is using 115200 bps for
                   communication as a default but it can change.
                   Parity :Can be None-Odd-Even-Mark-Space. Printer is using None value for

                                          ~ 15 ~
                                 Savema Printer Programming Language (Rev.12)           03/08/2022

                    parity as a default but it can change.
                    Data Bits:Can be 5-6-7-8. Printer is using 8 bits for data bits as a default
                    but it can change.
                    Stop Bits :Can be 1-1.5-2. Printer is using 1 bit for stop bits as a default but
                    it can change.

                    Return Value(On Successed) :
                    ~ SPGRES{ SPCSSC:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{SPCSSC:FAIL}^

      Example       ~SPCSSC{115200>None>8>1}^


         SPCGSC:Returns RS-232(Serial) parameters of printer.

      Using         ~SPCGSC^
      Example       ~SPCGSC^

                    Return Value(On Successed) :
                    ~ SPGRES{ SPCGSC:115200<None<8<1}^



2.4      Set/Get Print Speed(Intermitttent only)
         SPCSPS: Allows to set print speed(mm/sec) of printer. Printers speed is adjusting for
         intermittent models because continuous type printers gets print speed from encoder
         and encoder gets speed from media(package,label…etc) flowing speed.
         Printer sends OK message when setting print speed is successed or sends FAIL
         message when setting print speed is failed.


      Using         ~SPCSPS{Print Speed}^

                Parameters;
                   Print Speed:Specified according to millimeter per second. Print speed
                   value must be between 150-400.
                   Note : If user want to print on hard meda or use resin type ribbon, print
                   speed must be maximum 200. Please look at Service manual of related
                   printer for more information.

                    Return Value(On Successed) :
                    ~ SPGRES{ SPCSPS:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{ SPCSPS:FAIL}^

      Example       ~SPCSPS{200}^

                                            ~ 16 ~
                                 Savema Printer Programming Language (Rev.12)           03/08/2022



         SPCGPS : Returns print speed of printer..

      Using         ~SPCGPS^

      Example       ~SPCGPS^

                    Return Value(On Successed) :
                    ~ SPGRES{ SPCGPS:200}^



2.5      Set/Get Print Delay value
         SPCSPD : Allows to set print delay after print signal arrives. Specified in millimeter for
         continous models and specified in milliseconds for intermittent models.
         Printer sends OK message when setting print delay is successed or sends FAIL
         message when setting print delay is failed.
         This value changes after printer starts to print.

      Using         ~SPCSPD{Print Delay}^

                Parameters;
                   Print Delay:Print Delay is start of print time after print signal
                   arrives.Specified in millimeter(continuous) or millisecond(intermittent).

                    Return Value(On Successed) :
                    ~ SPGRES{ SPCSPD:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{ SPCSPD:FAIL}^

      Example       ~SPCSPD{10}^


         SPCGPD : Returns print delay of printer.

      Using         ~SPCGPD^

      Example       ~SPCGPD^

                    Return Value(On Successed) :
                    ~ SPGRES{ SPCGPD:0}^




                                             ~ 17 ~
                                Savema Printer Programming Language (Rev.12)       03/08/2022

2.6      Set/Get Darkness(Contrast) Value

         SPCSDV : Allows to set print darkness(contrast) of printer. Darkness can change
         according to media type. Can be increased when print quality is not good on media
         and also can increase ribbon type.
         Printer sends OK message when setting darkness is successed or sends FAIL message
         when setting darkness is failed.
         This value changes after printer starts to print.


      Using         ~SPCSDV{Contrast}^

                Parameters;
                   Contrast: Darkness(Contrast) value must be between 60-120.

                    Return Value(On Successed) :
                    ~ SPGRES{PCSDV:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{PCSDV:FAIL}^

      Example       ~SPCSDV{100}^


         SPCGDV : Returns darkness(contrast) value of printer.

      Using         ~SPCGDV^

      Example       ~SPCGDV^

                    Return Value(On Successed) :
                    ~ SPGRES{ SPCGDV:100}^



2.7      Set/Get Print Rotation
         SPCSPR : Allows to adjust print rotation of template.
         Printer sends OK message when setting print rotation is successed or sends FAIL
         message when setting print rotation is failed.

      Using         ~SPCSPR{Print Rotation}^

                Parameters;
                   Print Rotation:Can be 0-90-180-270.
                   0 : Print direction is normal
                   90 :Print directionis 90 degrees of clockwise
                   180 : Print direction is reverse

                                           ~ 18 ~
                                  Savema Printer Programming Language (Rev.12)          03/08/2022

                     270 : Print directionis 270 degrees od clockwise or 90 degrees of anti-
                     clockwise
                     Note : Template height cannot be higher than printhead size while using
                     90 or 270 print direction or vice versa.
                      For 32 mm models, template height must be maximum 32mm.
                     For 53 mm models, template height must be maximum 53mm.
                     For 107 mm models, template height must be maximum 107mm
                     For 107x75I model, width is higher than height (107 > 75), 90 or 270
                     rotation doesn’t supported.

                     Return Value(On Successed) :
                     ~ SPGRES{ SPCSPR:OK}^
                     Return Value(On Failed) :
                     ~ SPGRES{ SPCSPR:FAIL}^

      Example        ~SPCSPR{180}^


         SPCGPR:Returns print rotation value template.

      Using          ~SPCGPR^

      Example        ~SPCGPR^

                     Return Value(On Successed) :
                     ~ SPGRES{SPCGPR:180}^



2.8      Set/Get Horizontal Position
          SPCSHP :Allows to set horizontal position of print. This command changes print
      location horizontally and moves print to right side. If print is moving to outside of print
      area, overflowing part printt left side.
      Printer sends OK message when setting horizontal position is successed or sends FAIL
      message when setting horizontal position is failed.
          This value changes after printer starts to print.



      Using          ~SPCSHP{Horizontal Position Value}^

                 Parameters;
                    Horizontal Position Value:Horiontal Position value must be start 0.
                    Maximum value of position changes according to print head types.
                    For 32 mm models, horizontal position can increase maximum 48.
                    For 53 mm models, horizontal position can increase maximum 80.
                    For 107 mm models, horizontal position can increase maximum 160.

                                             ~ 19 ~
                                Savema Printer Programming Language (Rev.12)      03/08/2022


                    Return Value(On Successed) :
                    ~ SPGRES{ SPCSHP:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{ SPCSHP:FAIL}^

      Example       ~SPCSHP{0}^


         SPCGHP :Returns horizontal position value of print.

      Using         ~SPCGHP^

      Example       ~SPCGHP^

                    Return Value(On Successed) :
                    ~ SPGRES{SPCGHP:0}^



2.9      Set/Get Vertical Position(It will be used in the future)
         SPCSVP(Not Used.)
         SPCGVP(Not used)


2.10 Set/Get Mirroring Option
          SPCSMO : Allows to print template mirrored.
      Printer sends OK message when setting mirroring option is successed or sends FAIL
      message when setting mirroring option is failed.

      Using         ~SPCSMO{Mirroring Option}^

                Parameters;
                   Mirroring Option: This paramter must be 0 or 1.
                   0: Mirroring is passive
                   1: Mirroring is active

                    Return Value(On Successed) :
                    ~ SPGRES{ SPCSMO:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{ SPCSMO:FAIL}^

      Example       ~SPCSMO{0}^




                                           ~ 20 ~
                            Savema Printer Programming Language (Rev.12)          03/08/2022

     SPCGMO : Returns mirroring active or passive of print.

  Using         ~SPCGMO^

  Example       ~SPCGMO^

                Return Value(On Successed) :
                ~ SPGRES{ SPCGMO:0}^



2.11 Set/Get RibbonSave Mode
     SPCSRS : Allows to print more than one columns on same vertical or horizontal
     position. There is two-type RibbonSave mode. These are;
     Vertical :Vertical RibbonSave mode must be used when template widthsmaller than
     half of printhead size. Otherwise printer prints on of another print in same vertical
     position. Look at ribbonsave schema in below.
     Horizontal : (For only Intermittent models)If template objects has vertical gaps,
     Horizontal RibbonSave mode reduces ribbon consumption.Colıumn no and Shifting
     lengh should be adjust according to between objects gaps. Otherwise printer prints
     on of another print after gaps in same horizontal position.Look at ribbonsave schema
     in below.

     Please look at Service manual of related printer for more information.
     Printer sends OK message when setting ribbonsave mode is successed or sends FAIL
     message when setting ribbonsave mode is failed.


  Using         ~SPCSRS{Direction>Column No>Shifting length }^

            Parameters;
               Direction: Should be 0 or 1 . 0 is Vertical RibbonSave mode, 1 is
               Horizontal RibbonSaveMode(This is only for intermittent printers). 0 is
               Default value.
               Column No:Provides to select print count on same vertical position. 1 is
               default. 1 mean only one print in same vertical position.Can increase
               according to print width. İf it is higher than half of printhead size, it must
               be 1. Forexample, if printhead size is 53 mm and print width is 10mm, Can
               increase upto 5.
               Shifting length :Specifies distance between two prints for more than one
               columns. Specified in millimeter.
               Parameter must adjust according to print width. Forexample, if printhead
               is 53 mm and print width 8 mm, shifing length must be 3 mm. Otherwise,
               prirnter prints upon another print.This value starts from 0, end value can
               be increased upto appropriate value.


                                       ~ 21 ~
                            Savema Printer Programming Language (Rev.12)        03/08/2022

                Return Value(On Successed) :
                ~ SPGRES{SPCSRS:OK}^
                Return Value(On Failed) :
                ~ SPGRES{SPCSRS:FAIL}^

  Example       ~SPCSRS{0>1>0}^ - For 1 column- No RibbonSave

                ~SPCSRS{0>2>4}^ - For 2 columns and Columns distance is 4 mm



     SPCGRS : Returns RibbonSave Mode parameters.

  Using         ~SPCGRS^

  Example       ~SPCGRS^

                Return Value(On Successed) :
                ~ SPGRES{ SPCGRS:0<1<0}^




                        Figure : Exmaple scheme of Ribbonsave modes


2.12 Set/Get Internal Contact Mode(Continuous only)
     SPCSIC : This command provides to printer prints without external print signal. This
     command is only using continuos printer and this type of printer prints at regular
     intervals without print signal (from photocell, pack machine..etc)
     Printer sends OK message when setting internal contact mode is successed or sends
     FAIL message when setting internal contact mode is failed.




                                       ~ 22 ~
                            Savema Printer Programming Language (Rev.12)        03/08/2022

  Using         ~SPCSIC{Internal Contact Mode State> Package length }^

            Parameters;
               Internal Contact Mode State:To enable or disable Internal contact mode.
               This value must be 0 or 1.
                  0 : Disable
                  1 : Enable
               Package length :Specifies on package length for print. Printer prints at
               regular intervals according to package length. Specified in millimeter. This
               value must be between 35-1000

                Note: Internal Contact mode doesn’t work with Trigger Contact mode. So,
                if Internal Contact will be enabled, Trigger Contact Mode must be
                disabled.

                Return Value(On Successed) :
                ~ SPGRES{ SPCSIC:OK}^
                Return Value(On Failed) :
                ~ SPGRES{ SPCSIC:FAIL}^

  Example       ~SPCSIC{1<100}^ - Printer prints per 100 mm without print signal

                ~SPCSIC{0<100}^ - Printer prints when external print signal comes

     SPCGIC : Returns Internal Contact Mode parameters.

  Using         ~SPCGIC^

  Example       ~SPCGIC^

                Return Value(On Successed) :
                ~ SPGRES{SPCGIC:1<200}^




2.13 Set/Get Trigger Contact Mode(Continuous only)
     SPCSTC : This command provides to printer, prints more than one in one print signal.
     Printer sends OK message when setting trigger contact mode is successed or sends
     FAIL message when setting trigger contact mode is failed.




                                       ~ 23 ~
                             Savema Printer Programming Language (Rev.12)        03/08/2022

   Using        ~SPCSTC{Trigger Contact Mode State>Print Count> Package length }^

             Parameters;
                Trigger Contact Mode State:To enable or disable Trigger contact mode.
                This value must be 0 or 1.
                   0 : Disable
                   1 : Enable
                Print Count : Specifies how many print per contact.
                Package lengh :Specifies on package length for print. Printer prints at
                regular intervals according to package length. Specified in millimeter. This
                value must be between 35-1000

                Note:Trigger Contact mode doesn’t work with Internal Contact mode. So,
                if Trigger Contact will be enabled, Internal Contact Mode must be
                disabled. Because trigger contact mode Works with external print signal.

                Return Value(On Successed) :
                ~ SPGRES{ SPCSTC:OK}^
                Return Value(On Failed) :
                ~ SPGRES{ SPCSTC:FAIL}^

   Example      ~SPCSTC{1>3>100}^ - Printer prints 3 times at 100 mm intervals after
                print signal comes

                ~SPCSTC{0>1>100}^ - Printer prints one time when external print signal
                comes

      SPCGTC: Returns Trigger Contact Mode parameters.

   Using        ~SPCGTC^

   Example      ~SPCGTC^

                Return Value(On Successed) :
                ~ SPGRES{SPCGTC:1<2<200}^

2.14 Set/Get All Settings
      SPCSAS : Allows to set below settings.
            1- Print Speed
            2- Print Delay
            3- Darkness Value
            4- RibbonSave Mode Direction
            5- RibbonSave Mode Column No
            6- RibbonSave Mode Package Length
            7- Internal Contact Mode State


                                        ~ 24 ~
                          Savema Printer Programming Language (Rev.12)       03/08/2022

          8- Internal Contact Mode Package Length
          9- Trigger Contact Mode State
          10- Trigger Contact Mode Print Count
          11- Trigger Contact Mode Package Length

    Some settings are used according to printer type. (eg: Internal and Contact Mode is
   usable only Continuous models, Print Speed is usable in only Intermittent models. ) 0
   value can be used for unused parameters, All settings must be sent in proper
   sequence. Otherwise printer doesn’t apply this settings.
   Printer sends OK message when setting all settings are successed or sends FAIL
   message when setting all settings are failed.


Using          ~SPCSAS{Print Speed>Print Delay>Darkness Value>RibbonSave Mode
               Direction> RibbonSave Mode Column No> RibbonSave Mode Package
               Length>Internal Contact Mode State>Internal Contact Mode Package
               Length>Trigger Contact Mode State>Trigger Contact Mode Print Count
               >Trigger Contact Mode Package Length}^

               Note: Above parameters explanation is showed with related settings
               command. So, please see related commands for more details.
               Forexample, for Print Speed look at SPCSPS(Set Print Speed) command
               expalanation.

               Return Value(On Successed) :
               ~ SPGRES{ SPCSAS:OK}^
               Return Value(On Failed) :
               ~ SPGRES{ SPCSAS:FAIL}^

Example
               ~SPCSAS{300>2>100>0>1>0>0>30>1>3>60}^ - Set all system settings
               according to specified values.




   SPCGPA : Returns all system settings value.

Using         ~SPCGAS^

Example       ~SPCGAS^

              Return Value(On Successed) :
              ~ SPGRES{SPCGAS:300<2<100<0<1<0<0<30<1<3<60}^ -- All system
              parameters are returned with SPGRES command.



                                     ~ 25 ~
                           Savema Printer Programming Language (Rev.12)      03/08/2022



2.15 Set/Get System Parameter
     SPCSSP :Allows to set selected system parameters. Savema Printers have 20 pieces of
     parameters and each parameter function is changing according to printer type.
     Please look at System Paramaters Explanation for more info at the end of this
     document.
     Printer sends OK message when setting selected system parameter is successed or
     sends FAIL message when setting selected system parameter is failed.

  Using        ~SPCSSP{Parameter No>Parameter value}^

            Parameters;
               Parameter No:Specifies parameter number which will be changed. Value
               must be between 1 -20.
               Parameter Value :Specifies parameter value of selected system
               parameter. This parameter’s minimum and maximum value is changing
               according to parameter function and printer type. Please look at System
               Paramaters Explanation for more info at the end of this document.

               Return Value(On Successed) :
               ~ SPGRES{ SPCSSP:OK}^
               Return Value(On Failed) :
               ~ SPGRES{ SPCSSP:FAIL}^

  Example      ~SPCSSP{1>25}^ - Set First System Parameter to 25

               ~SPCSSP{15>20}^ - Set 15th System Parameter to 20


     SPCGSP : Returns selected system parameter value.

  Using        ~SPCGSP{Parameter No }^

            Parameters;
               Parameter No:Specifies parameter number which will be changed. Value
               must be between 1 -20.

  Example      ~SPCGSP{1}^

               Return Value(On Successed) :
               ~ SPGRES{SPCGSP:25}^ -- First system parameter is 25.




                                      ~ 26 ~
                           Savema Printer Programming Language (Rev.12)      03/08/2022

2.16 Set/Get All System Parameters
     SPCSPA :Allows to set all system parameters. Savema Printers have 20 pieces of
     parameters and each parameter function is changing according to printer type. All
     parameters must be sent in proper sequence. Otherwise printer doesn’t apply this
     parameters. Please look at System Paramaters Explanation for more info at the end
     of this document.
     Printer sends OK message when setting all system parameters is successed or sends
     FAIL message when settingall system parameters is failed.


  Using         ~SPCSPA{P1>P2>P3>P4>P5>P6>P7>P8>P9>P10>P11>P12>P13>P14>P15
                >P16>P17> P18>P19>P20}^
                P means Parameter
             Parameters;
                Parameter Values :Specifies parameter values of all system parameters.
                Minimum and maximum value is changing according to parameter
                function and printer type. Please look at System Paramaters Explanation
                for more info at the end of this document.
                Note:Affects printer working, so be carefull while setting parameter
                values.

                Return Value(On Successed) :
                ~ SPGRES{ SPCSPA:OK}^
                Return Value(On Failed) :
                ~ SPGRES{ SPCSPA:FAIL}^

  Example
                ~SPCSPA{25>27>300>200>31>77>0>24>25>0>1265>0>5>0>23>0>4>0>0>400}
                ^ - Set all system parameters according to specified values.




     SPCGPA : Returnsall system parameters value.

  Using         ~SPCGPA^

  Example       ~SPCGPA^

                Return Value(On Successed) :
                ~
                SPGRES{SPCGPA:25<27<300<200<31<77<0<24<25<0<1265<0<5<0<23<0<4<0<
                0<400}^ -- All system parameters are returned with SPGRES command.




                                      ~ 27 ~
                              Savema Printer Programming Language (Rev.12)         03/08/2022

2.17 Set/Get One Additional Settings
      SPCSOA : Allows to set selected additional settings . Additional settings will be used
      for general purposes.
      Printer sends OK message when setting selected additional settings is successed or
      sends FAIL message when setting selected additional settings is failed.

   Using         ~SPCSOA{Parameter No>Parameter value}^

             Parameters;
                Parameter No:Specifies parameter number which will be changed. Value
                must be between 1 -20.
                Parameter Value :Specifies parameter value of selected additional
                settings . This parameter’s minimum and maximum value are between 0-
                3000.

                 Return Value(On Successed) :
                 ~ SPGRES{ SPCSOA:OK}^
                 Return Value(On Failed) :
                 ~ SPGRES{ SPCSOA:FAIL}^

   Example       ~SPCSOA{1>25}^ - Set First additional setting to 25

                 ~SPCSOA{15>20}^ - Set 15th additional settings to 20


      SPCGOA : Returns selected additional settings value.

   Using         ~SPCGOA{Parameter No }^

             Parameters;
                Parameter No:Specifies parameter number which will be changed. Value
                must be between 1 -20.

   Example       ~SPCGOA{1}^

                 Return Value(On Successed) :
                 ~ SPGRES{SPCGOA:25}^ -- First system parameter is 25.



2.18 Set/Get All Additional Settings
      SPCSAA : Allows to set all additional settings. Additional settings have 20 pieces of
      parameters. All parameters must be sent in proper sequence. Otherwise printer
      doesn’t apply this parameters.
      Printer sends OK message when setting all additional settings is successed or sends
      FAIL message when setting all additional settings are failed.

                                         ~ 28 ~
                            Savema Printer Programming Language (Rev.12)        03/08/2022




  Using         ~SPCSAA{P1>P2>P3>P4>P5>P6>P7>P8>P9>P10>P11>P12>P13>P14>P15
                >P16>P17> P18>P19>P20}^
                P means Parameter
             Parameters;
                Parameter Values :Specifies parameter values of all additional settings
                Minimum and maximum value are between 0-3000. Please look at
                System
                Note:Affects printer working, so be carefull while setting parameter
                values.

                 Return Value(On Successed) :
                 ~ SPGRES{ SPCSAA:OK}^
                 Return Value(On Failed) :
                 ~ SPGRES{ SPCSAA:FAIL}^

  Example
                 ~SPCSAA{10>20>30>40>50>60>150>300>685>1150>1265>24>890>0>23>100>
                 4>54>32>400}^ - Set all additional settings according to specified values.




     SPCGAA : Returns all additional settings value.

  Using          ~SPCGAA^

  Example        ~SPCGAA^

                 Return Value(On Successed) :
                 ~
                 SPGRES{SPCGAA:10<20<30<40<50<60<150<300<685<1150<1265<24<890<0<2
                 3<100<4<54<32<400}^ -- All additional settings are returned with SPGRES
                 command.



2.19 Set /Get System Language
     SPCSSL : Allows to change System Interface Language.
     Printer sends OK message when setting system language is successed or sends FAIL
     message when setting system language is failed. Not

  Using         ~SPCSSL{System Languge Code }^

            Parameters;
               System Language Code:Specifies system interface language. Not used
               codes, can be used later. Now, if language code bigger than 18, printer

                                       ~ 29 ~
                         Savema Printer Programming Language (Rev.12)      03/08/2022

             turns system language to English as a default. Codes are shown in below;
             01 : Turkish
             02 : English
             03 : Arabic
             04 : German
             05 : Russian
             06 : French
             07 : Spanish
             08 : Italian
             09 :Czech
             10 :Dutch
             11 : Chinese
             12 : Korean
             13 : Portuguese
             14 : Sinhala
             15 : Hebrew
             16 :Polish
             17 : Greek
             18 : Persian
             19: Lithuanian
             20: Finnish
             21 : Not used
              .
              .
              .
             50 : Not used


             Return Value(On Successed) :
             ~ SPGRES{ SPCSSL:OK}^
             Return Value(On Failed) :
             ~ SPGRES{ SPCSSL:FAIL}^

Example      ~SPCSSL{01}^ -- Set system language to Turkish
             ~SPCSSL{02}^ --Set system language to English

   SPCGSL :Returns system Interface code. This codes are shown in above in SPCSSL
   command explanation.

Using        ~SPCGSL^

Example      ~SPCGSL^

             Return Value(On Successed) :
             ~ SPGRES{ SPCGSL:02}^ -- System Language is English



                                   ~ 30 ~
                            Savema Printer Programming Language (Rev.12)       03/08/2022

2.20 Set/Get Administrator Password
     SPCSAP : Allows to set system administrator password. Administrator password
     provides to be restriched some settings on printer. This password must be
     numerical. Otherwise printer doesn’t allow to change system password.
     Printer sends OK message when setting administrator password is successed or
     sends FAIL message when setting password is failed.

  Using         ~SPCSAP{System Password }^

            Parameters;
               System Password:Specifies system password. This password must be
               numerical.

                Return Value(On Successed) :
                ~ SPGRES{SPCSAP :OK}^
                Return Value(On Failed) :
                ~ SPGRES{SPCSAP :FAIL}^

  Example       ~SPCSAP{123456}^ - System Password is 123456


     SPCGAP: Returns system administrator password.

  Using         ~SPCGAP^

  Example       ~SPCGAP^

                Return Value(On Successed) :
                ~ SPGRES{SPCGAP:123456}^ -- Administrator password is 123456



2.21 Return to Factory Settings
     SPCSFS : Returns all parameters to factory setting. Stored templates and data files
     doens’t delete when return to factory settings. Please be carefull while using this
     command because all parameters deletes and load factory settings.
     Printer sends OK message when printer is returned to factory settings or sends FAIL
     message when return to factory settings is failed.

  Using         ~SPCSFS^

                Return Value(On Successed) :
                ~ SPGRES{SPCSFS:OK}^
                Return Value(On Failed) :
                ~ SPGRES{SPCSFS:FAIL}^
  Example       ~SPCSFS^ - System retuns to factory settings

                                       ~ 31 ~
                            Savema Printer Programming Language (Rev.12)         03/08/2022

2.22 Set/Get Print Request Message
     SPCSPM : This command provides printer to send message per print after print
     finished. Printer doesn’t send message per print as a default but if you activate this
     function with this command, printer sends message which is identified by you end of
     each print..


  Using         ~SPCSPM{Print Request Active|Passive>Print Message}^

            Parameters;
               Print Request Active|Passive:Specifies print request is active or passive. İt
               can be 0 or 1.
               0: Printer doesn’t send print message. This is default value.
               1: Printer sends message end of each print to connected device.

                Print Message :Specifies print message which be send to connected
                device. Print reques Message is OK as a default. It can be READY or
                another message. The message length should not exceed 10 characters.

                Return Value(On Successed) :
                ~ SPGRES{SPCSPM:OK}^
                Return Value(On Failed) :
                ~ SPGRES{SPCSPM:FAIL}^

  Example       ~SPCSPM{0>OK}^ - Printer doesn’t send message.

                ~SPCSPM{1>OK}^ - Printer sends ~ SPGRES{OK}^
                message end of each print.

                ~SPCSPM{1>READY }^ - Printer sends ~ SPGRES{READY}^
                message end of each print.


     SPCGPM: Returns print request situation and print message.

  Using         ~SPCGPM^

  Example       ~SPCGPM^

                Return Value(On Successed) :
                ~ SPGRES{SPCGPM:0<OK}^ --Print Request message is disabled.




                                       ~ 32 ~
                                Savema Printer Programming Language (Rev.12)       03/08/2022


3. Label Designing Commands
3.1      CreateTemplate Datas and Template Structure
         SPLTDS : This command creates template on printer side. This command parameter
         contains whole template data, so this command’s parameter can be very long. When
         this command is sended, printer save this template to its memory.
         Template data structure is created in xml format and this structure occurs two parts.
         First part contains general template datas. This datas specifies template general
         properties(Name, Printer Type, Width, Height).
         Second part contains object datas. This datas specifies object type, object name, X,Y
         position, rotation, font and specific object datas.
         Templates data structure is defined in below. Please look at in there.
         Printer sends OK message when creating templateoperation is successed or sends
         FAIL message when setting creating templateoperation is failed.
         Note : Template data transfer time changes according to communication type and
         speed. If template data is big, transfer time increases.

         Note : SPLKTD send template to printer storage but don't load , SPLTDS send
         template data to printer storage and load


      Using         ~SPLTDS{Template Data}^

                Parameters;
                   Template Datas:Creates whole template content. And this is created in
                   XML format. This can be very long.
                   Note: Template data must be created according to template data
                   structure rules. Otherwise it doesn’t created

                    Return Value(On Successed) :
                    ~ SPGRES{SPLTDS:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{SPLTDS:FAIL}^

      Example ~SPLTDS{<Template>
                       <General>
                            <MachineType>53x70I</MachineType>
                            <Name>temp1_53.rox</Name>
                            <Width>640</Width>
                            <Height>912</Height>
                            <ZIndex>-1000</ZIndex>
                           <SaveImages>False</SaveImages>

                                           ~ 33 ~
                              Savema Printer Programming Language (Rev.12)       03/08/2022

                          <DataSourcesInfo/>
                     </General>
                     <DataSources/>

                     <Object>
                          <ObjectType>Text</ObjectType>
                          <Name>text1</Name>
                           <NameID>Text01</NameID>
                          <X>10</X>
                          <Y>63</Y>
                          <W>105</W>
                          <H>33</H>
                          <ZIndex>0</ZIndex>
                          <Rotate>180</Rotate>
                          <Hidden>False</Hidden>
                          <Content>
                           <Data>savema Printer</Data>
                           <Source>Internal</Source>
                           <PromptMessage></PromptMessage>
                           <AllowedCharacters>Any</AllowedCharacters>
                          <MagnificationRatio>100</MagnificationRatio>
                           <Inverted>False</Inverted>
                           <Mirror>False</Mirror>
                          </Content>
                          <Font>
                                 <Name>Arial</Name>
                                 <Size>36</Size>
                                <OriginalSize>12</OriginalSize>
                                 <Style>Bold,Italic</Style>
                          </Font>
                     </Object>
                     <Dates/>
                  </Template>}^ --- This template has only one text


3.1.1     General Template Datas

This settings does not affect objects and only related with template general settings. Our
machine Print Head’s resolution is 300 Dpi. So measurements are determining according to
300 Dpi. Template settings are shown in below ;



                                        ~ 34 ~
                        Savema Printer Programming Language (Rev.12)    03/08/2022

MachineType : Specifies type of machine.Machine types affects width and maximum
height of template. Machine types are;



1- SVM 32 C (32mm Continuous): 125mm (4.92inch)
2- SVM 32 CK (32mm Continuous): 125mm (4.92inch)
3- SVM 32C XL (32mm Continuous): 125mm (4.92inch)
4- SVM 32*250 C (32mm Continuous): 250mm (9.84inch)
5- SVM 32*500 C(32mm Continuous): 500mm (19.68inch)
6- SVM 53 C (53mm Continuous): 125mm (4.92inch)
7- SVM 53 C XL (53mm Continuous): 125mm (4.92inch)
8- SVM 53*250 C (53mm Continuous): 250mm (9.84inch
9- SVM 53*500 C (53mm Continuous): 500mm (19.68inch)
10-SVM 107 C (107mm Continuous): 125mm (4.92inch)
11-SVM 107 C XL (107mm Continuous): 125mm (4.92inch)
12-SVM 107*250 C (107mm Continuous): 250mm (9.84inch)
13-SVM 128 C (128mm Continuous): 200mm (7.87inch)
14-SVM 32*70 I (32mm Intermittent): 76mm (2.95 inch)
15-SVM 32*50 I (32mm Intermittent): 50 mm (1.97 inch)
16-SVM 32*40 I (32mm Intermittent): 40 mm (1.57 inch)
17-SVM 32*70 I XL (32mm Intermittent): 76mm (2.95 inch)
18-SVM 53*50 I (53mm Intermittent):50 mm (1.97 inch)
19-SVM 53*40 I (53mm Intermittent):40 mm (1.57 inch)
20-SVM 53*70 I (53mm Intermittent):75mm (2.95 inch)
21-SVM 53*70 I XL (53mm Intermittent):75mm (2.95 inch)
22-SVM 53*125 I (53mm Intermittent):125mm (4.92 inch) 23-
SVM 107*75 I (107mm Intermittent): 75mm (2.95 inch) 24-
SVM 107*125 I (107mm Intermittent): 125mm (4.92 inch) 25-
SVM 107*75 I XL (107mm Intermittent): 75mm (2.95 inch)
26-SVM 128*125 I (128mm Intermittent): 125mm (4.92inch)
27-SVM TR32*250 (32mm ): 250mm (9.84inch)
28-SVM TR53*250 (53mm ):250mm (9.84inch)
29-SVM TR107*250 (107mm ): 250mm (9.84inch)
30-SVM TR32 (32mm ): 1000mm

                                  ~ 35 ~
                               Savema Printer Programming Language (Rev.12)         03/08/2022

   31-SVM TR53 (53mm ): 1000mm
   32-SVM TR107 (107mm ): 1000mm



   33- Name : Specifies name of template file.This name format is name_53[32,107].rox.
       For example temp1_53.rox, temp1 is name, _53 shows machine printhead widthand
       it can be 32 and 107, .rox is an extension of file name.
   34- Width : Specifies width of template. This is pixel value and 1 mm = 12 pixels(300 dpi)
       . It can be 384(32mm), 640(53mm), 1280(107mm).

   35- Height : Specifies height of template. This is pixel value and 1 mm = 12 pixels(300
       dpi). It changes minimum 12 pixel and maximum 1500 pixels. But maximum height is
       changing according to machine type.(For machine type, look at MachineType
       property.)



3.1.2      General Object Datas
Some properties are using for all objects.

   1- ObjectType : Specifies type of object. These are shown in below;
         a- Date
         b- Time
         c- Text
         d- Counter
         e- Logo
         f- Shape
         g- Barcode
         h- 2Dbarcode
   2- Name : Specifies name of object. Each object name must be different from the
      others. This names must be increased sequentally. For example
      date1,date2,..text1,text2..etc.

   3- NameId: Generate automatic from system (Text1 , Text2 …)

   4- X : Specifies X(horizontal) position of object. This is pixel value and 1 mm=12 pixels
      (300 dpi).

   5- Y: Specifies Y(vertical) position of object. This is pixel value and 1 mm= 12 pixels(300
      dpi).



                                             ~ 36 ~
                               Savema Printer Programming Language (Rev.12)          03/08/2022

   6- W : Specifies width of object. This is pixel value and 1 mm= 12 pixels(300 dpi).

   7- H : Specifies height of object. This is pixel value and 1 mm= 12 pixels(300 dpi).

   8- Rotate : Specifies rotation of object. Default it is 0. There are ;
       a- 0 : Default rotation of object.
       b- 90 : It turns object to clockwise.
       c- 180 : It turns object to reverse.
       d- 270 : It turns object to anticlockwise.
   9- Hidden : Specifies visibility of object. Values can be True or False. Default value is
       False.
           a. True : Object will be shown in printer controller but will be not printed.
           b. False : Object will be shown in printer controller and on print.
3.1.3     Objects(Content)
3.1.3.1     Date

Date object have various properties which is shown in below,

   1- Data : This item stores date value. For example 21.01.2017

   2- Format: This item stores format of date. Date object support many of different
      format. Generally it combines around these format type. For example for 21.01.2017
      , format items value is shown in ( ).
           a- dd : Short day value. ( 21 )
           b- ddd : Tree letter day value. ( Sat )
           c- dddd : Long day value. ( Saturday )
           d- MM : Short month value.( 01 )
           e- MMM : Tree letter month value. ( Jan )
           f- MMMM : Long month value. ( January )
           g- yy : Short year value. ( 17 )
           h- yyyy : Long year value. ( 2017 )
           i- jjj : Julian Date, Day of year . ( 021 )
           j- yjjj: Last digit of year and Julian date(7021)
           k- jjjy : Julian date and last digit of year(0217)
           l- DoW :Day sequence in a week. ( 6 )
           m- WWW : Week sequence in a year. ( 4 )


       For example if you use dd-MM-yyyy format, date appears 21.01.2017

   3- Separator : Stores separator which seperates date values.You can see it below;
      For example for 21.01.2017 ,


                                          ~ 37 ~
                           Savema Printer Programming Language (Rev.12)       03/08/2022

       a- Space ( ) : 21 01 2017
       b- Slash (/) : 21/01/2017
       c- Back Slash (\) : 21\01\2017
       d- Dot (.) : 21.01.2017
       e- Comma (,) : 21,01,2017
       f- Hypen (-) : 21-01-2017
       g- Colon (:) : 21:01:2017
       h- None () : 21012017

4- CountryCode : This item stores language country code for date. It is using date
   presentation.
   This code is changing according to country. Default language country is English(USA)
   and CountryCode is 1033 . You can find more info from this link:
   https://msdn.microsoft.com/en-us/library/ee825488%28v=cs.20%29.aspx.
   Country codes are shown under culture code. Culture code is shown as hexadecimal
   format. So, it must be convert to decimal format. Englsih- United States culture code
   is 0x0409 is table. Hex 0x0409 is same is 1033 in decimal format.

5- DayOffset : Specifies how many days will be add on actual date.

6- MonthOffset : Specifies how many months will be add on actual date.

7- YearOffset : Specifies how many years will be add on actual date.

8- Type : There are two type of dates.
       a- Actual : it changes automatically according to system date.
       b- Fixed : it doesn’t change according to system date. İt stores only saved date
           data from PC.
9- UpperCase : Specifies date character as Upper or lower
a- True : Date shown as 21 JAN 2017
            b- False : Date shown as 21 Jan 2017
10- UseSpecialMonthNames : Provides to use special names instead of standart Month
    names.
11- SpecialMonthNames : Contains special month names. Month names are 12 different
    words or letters and moth names must be seperated with – character.

Example ~SPLTDS{<Template>
                  <General>
                       <MachineType>53x70I</MachineType>
                       <Name>temp1_53.rox</Name>
                       <Width>640</Width>

                                        ~ 38 ~
          Savema Printer Programming Language (Rev.12)   03/08/2022

           <Height>912</Height>
          <ZIndex>-1000</ZIndex>
          <SaveImages>False</SaveImages>
          <DataSourcesInfo/>
    </General>
    <DataSources/>
    <Object>
           <ObjectType>Date</ObjectType>
           <Name>date1</Name>
           <NameID>Date01</NameID>
           <X>10</X>
           <Y>63</Y>
           <W>105</W>
           <H>33</H>
          <ZIndex>0</ZIndex>
           <Rotate>180</Rotate>
          <Hidden>False</Hidden>
           <Content>
                <Data>26/07/2022</Data>
<Source>Internal</Source>
<PromptMessage></PromptMessage>
<MagnificationRatio>100</MagnificationRatio>
<Format>dd/MM/yyyy</Format>
<UserDefinedFormat></UserDefinedFormat>
<Separator>/</Separator>
<CalculatedDate></CalculatedDate>
<CountryCode>1033</CountryCode>
<MinimumOffsetUnit>Disabled</MinimumOffsetUnit>
<MinimumOffset>0</MinimumOffset>
<MaximumOffset>0</MaximumOffset>
<DayOffset>0</DayOffset>
<MonthOffset>0</MonthOffset>
<YearOffset>0</YearOffset>
<Type>Actual</Type>
<UpperCase>False</UpperCase>
<UseSpecialMonthNames>False</UseSpecialMonthNames>
<SpecialMonthNames></SpecialMonthNames>
<Inverted>False</Inverted>
<Mirror>False</Mirror>
</Content>

                    ~ 39 ~
                               Savema Printer Programming Language (Rev.12)          03/08/2022

               <Font>
               <Name>Tahoma</Name>
               <Size>36</Size>
               <OriginalSize>12</OriginalSize>
               <Style>Regular</Style>
               </Font>
                    </Object>
                 <Dates/>
                 </Template>}^ --- This template has only one date


3.1.3.2       Time

Time object have various properties which is shown in below,

  1- Data : This item stores time value.For example 15:23
  2- Format: This item stores format of time. Time object support many of different
      format. Generally it combines around these format type. For example for 15:23:00 ,
      format items value is shown in ( ).
          a- HH : Hour value according to 24 hours . ( 15 )
          b- hh : Hour value according to 24 hours . ( 03 )
          c- mm : Minute value ( 23 )
          d- ss : Second value ( 00 )
          e- tt : Time symbol(AM/PM).It changes according to some country. ( 03:23 PM)

       For example if you use hh:mm tt format, time appears 03:23 PM

  3- Seperator : This item stores seperator which seperates time values.You can see it
     below;For example for 15:23,
           a- Space ( ) : 15 23
           b- Slash (/) : 15/23
           c- Back Slash (\) : 15\23
           d- Dot (.) : 15.23
           e- Comma (,) : 15,23
           f- Hypen (-) : 15-23
           g- Colon (:) : 15:23 - Default
           h- None () : 1523
  4- CountryCode : This item stores language country code for time. It is using time
     presentation. Especially it is useful for time symbol. if English(USA ) is selected, time
     symbol is AM/PM. This code is changing according to country. Default language
     country is English(USA) and CountryCode is 1033 . You can find more info from this
     link:
      https://msdn.microsoft.com/en-us/library/ee825488%28v=cs.20%29.aspx.

                                          ~ 40 ~
                            Savema Printer Programming Language (Rev.12)        03/08/2022

     Country codes are shown under culture code. Culture code is shown as hexadecimal
     format. So, it must be convert to decimal format. Englsih- United States culture code
     is 0x0409 is table. Hex 0x0409 is same is 1033 in decimal format.
5- HourOffset : Specifies how many hours will be add on actual time.
6- MinuteOffset : Specifies how many minutes will be add on actual time.
7- Type : There are two type of dates.
         a- Actual : it changes automatically according to system time.
         b- Fixed : it doesn’t change according to system time. İt stores only saved time
            data from PC.

Example ~SPLTDS{<Template>
                 <General>
            <MachineType>53*70 I</MachineType>
            <Name>temp1_53.rox</Name>
            <Width>640</Width>
            <Height>912</Height>
            <ZIndex>-1000</ZIndex>
            <SaveImages>False</SaveImages>
            <DataSourcesInfo/>
            </General>
            <DataSources/>
            <Object>
            <ObjectType>Time</ObjectType>
            <NameID>Time02</NameID>
            <Name>time1</Name>
            <X>75</X>
            <Y>111</Y>
            <W>132</W>
            <H>63</H>
            <ZIndex>0</ZIndex>
            <Rotate>0</Rotate>
            <Hidden>False</Hidden>
            <Content>
             <Data>11:07</Data>
             <Source>Internal</Source>
             <MagnificationRatio>100</MagnificationRatio>
             <Format>HH:mm</Format>
             <UserDefinedFormat></UserDefinedFormat>
             <Separator>:</Separator>
             <CountryCode>1033</CountryCode>
             <HourOffset>0</HourOffset>

                                      ~ 41 ~
                               Savema Printer Programming Language (Rev.12)          03/08/2022

                <MinuteOffset>0</MinuteOffset>
                <Type>Actual</Type>
                <Inverted>False</Inverted>
                <Mirror>False</Mirror>
               </Content>
               <Font>
                <Name>Tahoma</Name>
                <Size>36</Size>
                <OriginalSize>12</OriginalSize>
                <Style>Regular</Style>
               </Font>
               </Object>
               <Dates/>
                  </Template>}^ --- This template has only one text



3.1.3.3       Text

Text object have two different properties which is shown in below,

  1- Data : This item stores text value which is saved by PC. It uses ~ character for
     seperating lines for multiline text. For example if this item value is savema~printer,
     printer shows
      savema
      printer
     in screen. According to this sample, savema is first line, printer is second line.

  2- Source : This item specify text value source. it can be Internal and External.
         a- Internal : This is default selection for text. Text value is identified from PC
             when creating a template in this mode.
         b- External : Text object gets value from RS-232 or Ethernet interface.

  3- MagnificationRatio : This item stores magnification ratio of width. This ratio is shown
     in percentage. For Normal width This value must be 100 (100%). For bigger width of
     text, this value must be bigger than 100 and it can change according to magnification.

  4- Inverted : Text will be print inverted when adjus this item True. This item is set False
     as a default.

   Example ~SPLTDS{<Template>
           <General>
            <MachineType>53*70 I</MachineType>

                                          ~ 42 ~
                              Savema Printer Programming Language (Rev.12)      03/08/2022

               <Name>temp1_53.rox</Name>
               <Width>640</Width>
               <Height>912</Height>
               <ZIndex>-1000</ZIndex>
               <SaveImages>False</SaveImages>
               <DataSourcesInfo/>
              </General>
              <DataSources/>
              <Object>
               <ObjectType>Text</ObjectType>
               <NameID>Text03</NameID>
               <Name>text1</Name>
               <X>154</X>
               <Y>120</Y>
               <W>99</W>
               <H>63</H>
               <ZIndex>0</ZIndex>
               <Rotate>0</Rotate>
               <Hidden>False</Hidden>
               <Content>
               <Data>Text</Data>
               <Source>Internal</Source>
               <PromptMessage></PromptMessage>
               <AllowedCharacters>Any</AllowedCharacters>
               <MagnificationRatio>100</MagnificationRatio>
               <Inverted>False</Inverted>
               <Mirror>False</Mirror>
               </Content>
               <Font>
               <Name>Tahoma</Name>
               <Size>36</Size>
               <OriginalSize>12</OriginalSize>
               <Style>Regular</Style>
               </Font>
              </Object>
              <Dates/>
              </Template>}^ --- This template has only one text




3.1.3.4       RichText

RichText object have two different properties which is shown in below,

  1- HtmlData : This item stores html value of entered richtext which is saved by PC
     Software.

                                        ~ 43 ~
                           Savema Printer Programming Language (Rev.12)      03/08/2022

2- ImageData: This item storages RichText image's string in Base64 standart. Images are
   must be convert to Base64 data for this parameter.


Example      ~SPLTDS{<Template>
                   <General>
                         <MachineType>53*70 I</MachineType>
              <Name>temp1_53.rox</Name>
              <Width>640</Width>
              <Height>912</Height>
              <ZIndex>-1000</ZIndex>
              <SaveImages>False</SaveImages>
              <DataSourcesInfo/>
              </General>
              <DataSources/>
              <Object>
              <ObjectType>RichText</ObjectType>
              <NameID>TextBlock04</NameID>
              <Name>richtext1</Name>
              <X>171</X>
              <Y>168</Y>
              <W>300</W>
              <H>240</H>
              <ZIndex>0</ZIndex>
              <Rotate>0</Rotate>
              <Hidden>False</Hidden>
              <Content>
               <Source>Internal</Source>
               <MagnificationRatio>0</MagnificationRatio>

             <ImageData>iVBORw0KGgoAAAANSUhEUgAAATAAAADyCAIAAA
             BFzg6vAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAJJ0lEQVR4nO3
             dMXabSACHcbxvjyKlyMsJ4AQoTaq06VApNe5SpksDpdSldeXGcAL
             pBH4uMtxFW9iJGRiGQULS3+vv12WjCIT5FhhG+OZwOEQANPxz7
             RUA8IogASEECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASEECQghSE
             AIQQJChoOsq6JYJkmS3FiSJEmWy6KoqvoCqwm8Dzf9X7+qq+Lnj/
             V2H/Q+cZZ/v12ls+nWDHiHeoKsq+W3RWCLTVl52KSnrxXwTv3r+
             G91kczX42MEcKruNWS17NQYZ3leGnN4ZYwpyzyL4wutJvBOHGxl
             Zv91nJXm4GXKRpdZ6X8xAB/7CFkXP7bNP2flbjM0UDNLN7udKX
             MOlsDJrCDrh7vmyWpWBg/QzNLV7mCIEjhNc5S1NZjDiClwaReaq
             VPXVVUUS8cEgz8zDKr6ehMM6roqlq01S5LkhJWq68p6Q9+bvSy9

                                     ~ 44 ~
          Savema Printer Programming Language (Rev.12)   03/08/2022

veSwpQhvVRylcT3ZOuWcZoBmzHlsnOWOISR7nCnOBwaZepbs/jS
mzIdGin3DWtYiXtbMdzkdZ621N3nW/2LPJz19q0KTJ8jwXd9n9IVl
d7HHFWn/K0ePpuyPobNPu//n1Aky4NP+Xf8xrz3DVoUk67ZH657
HJD/EY0Z62rv/MUUO9di+v3PUHm0HGQfels3K8M3S82Gn2KpQZ
AXZ/TGf/kNsvGccZ3lZms4Mg+4Eg/ZuaK9YSJFWb51/0PmgcWbPf
Hheq8E2+rqwzhJN91Dc/LzWoh2vdf4IJtmqENSaGOA4bgxPDfAye
RxyEdPetVu74dgivT2Gn5oPXVU7guxcJjpWqLFo14YJuJafZqtCT3u
mTu+5UHf63MT8zY0bcPL1OLLtMWmPaLv/unT8KvpM+Fa4hHaQ
h4MZvLqKzxPnwKjo4CBNz0t9PY4++fUduAfebMT6Bw0QB5rwrXA
B3fuQs3QzMP6432/X68V8/nx/bbJbXbMPn3x/nX5p7tHb+6r3ldV
9Y/5f9n3VnPtnTUZq/V3Acn2LHTD/eJV5TANbFWKcEwNm6Wbn
GNVw2O+3i/n8JlkW578BHVikPR83+2JNNrJ7/BI0Eckq6fH3Je602x
VdZpmQ4Po+ZBRFUTRLV5t0dVtXDz9/3G33/q9H7rfr+fYuN7uAI0
4URXVdGRP9vr9/iqLHx8coivYDC4ii6LnI7d/YtvfVJu0WZc/HbTdnnl
7/Mv44D1nZ5zpO/3roRG/T78itCi3BJ7emLPNs4BuQ3ksU10j8yLc
aviDyX9VNMP3dWuioC1Lfxaj3td43nmCrQkj4XNZZmq42m93ucD
gcTJk7T2i3i6XrRLKuiiS5mS/WQ0fawXX4/LWxVMdZq3V8jL9+bh2
wmwfIN2+yrQohx00un6Wr1Wbnmra5/dG+mqyWyXyxnmifaRXZ
XthAj/8jk25V6Djp2x6zdLVr3/Le3z00IqmLxP2srDjOsizPy7IsjTHP80
xCZrPZRdoLO2II9W2afKtCRu+gTrD0No+3ja9R7p9MFD2nUBffuo/
nKX/dprPjU5mtvmfrxZ+hnf3dQ736E97IIdQ4eBBKyzm2KlRM8H1I
+6DVGKVvPYAginNz2G1O3m+s2x+NY2T1cz3U45XuBU7qTFsVGq
b4gnLPvefOA0GmOh65i7SnAwTcYtw/mUlW57LOtlUh4QxPDPj04
XkPsYc0A+/CB0lvG4NJL0U2e4zzW/fCBkdp9Z1vq0JBI8i6SJZH/aKO
+vdj40/u++2hd+HDWGHt1z8ru8f+4VX7YP4mi2yadqtCgH2E3C7m
yehJcPZJVE8NE58ftg91y7AeO/PvOjdp3pa3edYNj84p6349v0nCnr
EURVFnzK9Rgz2CMjQhsy6Sxdb7CkuryNcZdUO3O6zT3Wi//uacyu
Bcw6pYXmLOrt9Ztyqu73XSTvt7u9ngN6y6EwP6J5b5HhDjmsc+P
MnLfYMtYHLYMU9nf1nDU77LNdHUuTNvVVxXf5B/99Y8dzwgwvl
dkPaPe+hJGc4HTRwfVvge53owQvspHs7fXyIQ5Lm3Kq5qMMhgzv
1xzDQR+xlRIbtO992Dd7gjP6xCkOfeqrimxjXk7POv8Acjtn7qWeme
9ZJuwvb8OCvN7vvYr9LaIzSe2x1ds9VuxGMgxZx3q+KamoM6s1m
62b08+Sx4V42zvDSeX8kzuOfHceZ9Ax97hGbkbPLwr2FHL2tpDip3
4c+6VXFNnqPnn29Adp83Gscvz9UJPxR3LmzioOemXYYxL5+08zHP
/WivE0lvVRyh51eaA7iGC/2yHQAhCBIQQpCAEIIEhBAkIIQgASEEC
QghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASEECQghSEAIQQJCCBIQQ
pCAEIIEhBAkIIQgASEECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASE
ECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASEECQghSEAIQQJCCBIQ
QpCAEIIEhBAkIIQgASEECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgAS
EECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASEECQghSEAIQQJCCBI

                    ~ 45 ~
                             Savema Printer Programming Language (Rev.12)   03/08/2022

                QQpCAEIIEhBAkIIQgASEECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQg
                ASEECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASEECQghSEAIQQJC
                CBIQQpCAEIIEhBAkIIQgASEECQghSEAIQQJCCBIQQpCAEIIEhBAkII
                QgASEECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASEECQghSEAIQQ
                JCCBIQQpCAEIIEhBAkIIQgASEECQghSEAIQQJCCBIQQpCAEIIEhBA
                kIIQgASEECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASEECQghSEAI
                QQJCCBIQQpCAEIIEhBAkIIQgASEECQghSEAIQQJCCBIQQpCAEIIEh
                BAkIIQgASEECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASEECQghSE
                AIQQJCCBIQQpCAEIIEhBAkIIQgASEECQghSEAIQQJCCBIQQpCAEII
                EhBAkIIQgASEECQghSEAIQQJCCBIQQpCAEIIEhBAkIIQgASEECQgh
                SEAIQQJCCBIQQpCAEIIEhBAkIIQgASH/AX0+iuzOHG74AAAAAElFT
                kSuQmCC</ImageData>
                  <Inverted>False</Inverted>
                  <Mirror>False</Mirror>
                  <Html>&lt;!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML
                4.0//EN" "http://www.w3.org/TR/REC-html40/strict.dtd">
                &lt;html>&lt;head>&lt;meta name="qrichtext" content="1"
                />&lt;style type="text/css">
                p, li { white-space: pre-wrap; }
                &lt;/style>&lt;/head>&lt;body style=" font-family:'Tahoma';
                font-size:8pt; font-weight:400; font-style:normal;">
                &lt;p style=" margin-top:0px; margin-bottom:0px; margin-
                left:0px; margin-right:0px; -qt-block-indent:0; text-
                indent:0px;">&lt;span style=" font-
                size:12pt;">Savema&lt;/span>&lt;/p>&lt;/body>&lt;/html></Ht
                ml>
                 </Content>
                 <Font>
                  <Name>Arial</Name>
                  <Size>18</Size>
                  <Style>Regular</Style>
                 </Font>
                 </Object>
                 <Dates/>
                </Template>}^ --- This template has only one richtext



3.1.3.5       Counter

Counter object have various properties which is shown in below,

                                        ~ 46 ~
                            Savema Printer Programming Language (Rev.12)         03/08/2022

1- CounterType : This item specify type of counter. There are three counter types which
   is used.
            a- Numeric : it uses only numbers. For example, 1,2,3…999..etc
            b- Alphabetic : it uses only alpha. For example A,B,C,…ZZ…etc.
            c- AlphaNumeric : it combines alpha and numbers with together. Numbers
                are changing firstly and after end of numbers alpha is changing .For
                example AA000,AA001,…,AA999,AB000,AB001…..ZZ999

2- IncreasingDecreasing : it shows counter value is increasing or decreasing. It can be
   Increasing or Decreasing.
    a- Increasing : Counter starts small value and goes to big value. For example,
       001,002…999
    b- Decreasing : Counter starts big value and goes to smal value. 999,998,…002,001

3- Data: Counter value

4- NumericBegin : Numeric counter beginning value. For example 0000.

5- NumericEnd : Numeric counter ending value. For example 9999.
6- NumericStep : Numeric counter step value. Default it is 1. For 1, counter increases or
   decrease one by one. For example,1,2,3…999

7- NumericPeriod : Numeric counter period value. it shows counter increase or decrease
   after how many print. For example if this value is 3, printer prints same value 3 times.
   (1,1,1,2,2,2)

8- NumericDigit : Counter numeric digit. For example if it is 4, counter value show 4
   digits.

9- AlphaBegin : Alphabetic counter beginning value. For example AAA

10- AlphaEnd : Alphabetic counter ending value. For example ZZZ

11- AlphaStep : Alphabetic counter step value. Default it is 1. For 1, counter increases or
    decrease one by one. For example,AAA,AAB…..ZZZ

12- AlphaPeriod : Alphabetic counter period value. it shows counter increase or decrease
    after how many print. For example if this value is 3, printer prints same value 3 times.
    (AAA,AAA,AAA,AAB,AAB,AAB……ZZZ,ZZZ,ZZZ)
13- AlphaDigit : Counter alpha digit. For example if it is 3, counter value show 3 digits.
    (AAA)

                                       ~ 47 ~
                            Savema Printer Programming Language (Rev.12)        03/08/2022

14- AlphaChar : Padding character for alphabetic counter.it is using before counter value.
    Default it is A.So, counter shows AAA instead of A.
15- Restart : It can be True or False. When counter value arrives end value, it shows this
    value is turn to beginning value or not. (stay last value).

Example ~SPLTDS{<Template>
                 <General>
            <MachineType>53*70 I</MachineType>
            <Name>temp1_53.rox</Name>
            <Width>640</Width>
            <Height>912</Height>
            <ZIndex>-1000</ZIndex>
            <SaveImages>False</SaveImages>
            <DataSourcesInfo/>
            </General>
            <DataSources/>
            <Object>
            <ObjectType>Counter</ObjectType>
            <NameID>Counter05</NameID>
            <Name>counter1</Name>
            <X>129</X>
            <Y>84</Y>
            <W>114</W>
            <H>63</H>
            <ZIndex>0</ZIndex>
            <Rotate>0</Rotate>
            <Hidden>False</Hidden>
            <Content>
             <Data>0000</Data>
             <Source>Internal</Source>
             <MagnificationRatio>100</MagnificationRatio>
             <CounterType>Numeric</CounterType>
             <IncreasingDecreasing>Increasing</IncreasingDecreasing>
             <PromptMessage></PromptMessage>
             <NumericBegin>0</NumericBegin>
             <NumericEnd>9999</NumericEnd>
             <NumericStep>1</NumericStep>
             <NumericPeriod>1</NumericPeriod>
             <NumericDigit>4</NumericDigit>
             <AlphaBegin>A</AlphaBegin>
             <AlphaEnd>ZZ</AlphaEnd>

                                      ~ 48 ~
                              Savema Printer Programming Language (Rev.12)      03/08/2022

                <AlphaStep>1</AlphaStep>
                <AlphaPeriod>1</AlphaPeriod>
                <AlphaDigit>2</AlphaDigit>
                <AlphaChar>A</AlphaChar>
                <Restart>True</Restart>
                <Inverted>False</Inverted>
                <Mirror>False</Mirror>
               </Content>
               <Font>
                <Name>Tahoma</Name>
                <Size>36</Size>
                <OriginalSize>12</OriginalSize>
                <Style>Regular</Style>
               </Font>
               </Object>
                  <Dates/>
                  </Template>}^ --- This template has only one counter


3.1.3.6       Logo(Image)

Logo object have two different properties which is shown in below,

  1- ImageData : This item storages image's string in Base64 standart. Images are must be
      convert to Base64 data fort his parameter.

   Example      ~SPLTDS{<Template>
                      <General>
                 <MachineType>53*70 I</MachineType>
                 <Name>temp1_53.rox</Name>
                 <Width>640</Width>
                 <Height>912</Height>
                 <ZIndex>-1000</ZIndex>
                 <SaveImages>False</SaveImages>
                 <DataSourcesInfo/>
                 </General>
                 <DataSources/>
                 <Object>
                 <ObjectType>Logo</ObjectType>
                 <NameID>Logo06</NameID>
                 <Name>logo1</Name>
                 <X>78</X>

                                        ~ 49 ~
         Savema Printer Programming Language (Rev.12)   03/08/2022

<Y>10</Y>
<W>501</W>
<H>750</H>
<ZIndex>0</ZIndex>
<Rotate>0</Rotate>
<OriginalRotate>0</OriginalRotate>
<Hidden>False</Hidden>
<Content>
<Source>Internal</Source>
<MagnificationRatio>0</MagnificationRatio>

<ImageData>iVBORw0KGgoAAAANSUhEUgAAAfUAAALuCAIAAA
A40xiDAAAACXBIWXMAAA7DAAAOwwHHb6hkAAAgAElEQVR4n
O3d3ZqrqqIt0NT6zvu/cp2LGjsrK1GjCAjd1q7mHJUfI9BFRPz5/f19
ABDnP1dvAABNyHeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEy
yXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8
gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPId
IJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyX
eATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk
3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJ
N8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXe
ATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3
wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN
8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeA
TPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3w
EyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B
8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPI
dIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyy
XeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8g
k3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJ
N8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXe
ATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3
wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN
8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeA
TPIdIJN8B8j0/67egDn8/Pxs/PX393fxla//3mIDKn4+kOfnzhmxndq
LLt9dh7b58q2FAnoztdwu33fm4/i7ZcaDE4P4qzxD1Yefn39ZdLRiD
/UrRpOc7xLwsW8n5P3qO9so8XkLuqAtP2b+vbXk5Pvgaf7sngyi0X
UCrrJW/2MKtyziX/3+/o7WDFub9deeL+w3VfZD9a36NGl5UVFkD
32NNnXGHPneoYz/vO2Nbt9b1xRlSoFG4+aT1vN2YlrQoPnev8L9n
bt1/tLOxixrFj1rY61Si6/e7czbcAbKd/Wvs3GKnro0pQ6maD5X5nt2

                   ~ 50 ~
          Savema Printer Programming Language (Rev.12)   03/08/2022

LWy6YxvtuimqLG+GakfPKjTUVjU1cqvpmu9JRT5modbaw2P+uhg
nZ3FkHN2T0uDVUG2neb5nlOJQZXZIxf0/706YWosWNHhRCo1am
uT77MUzQsG0VlxGd9g5I6gy3bvKllxu9jz5c0lxVMv3qcsgpiWUOV
N2n2ur3XxnFqvSguJ3/tQ589StmM7m+9S7O74xFKhboPbwG7u3
hUlTqEPxleT7pHvzoT0clHEd70J2YGei6c2BfLfvbqtp0ceUjr00lBnzq
v6dydn5rlXU1boOTFRe1kUZ34yR9aj7XKDIfNcwmrqkJlxbpp1/sgp
c3T3ja2++T7F3tIrOBqkVa6vCTdd3UYFbG6Sgv6o2rTEm37WNq4xf
Nwan6vY3eKWtVSWmf762tnG5Gy45Uovae5WyZwFOp1++t0gBz
WMod1hjuSK193ItauznHX8X6jQ+0+K5GZrHsC6v1iNTbwfUc2G+n
o9E7jr+XutoqYVMQcp/UnWHVTGavl7h//pdU+Z7FVrIRMapNpdT
b6cwSI2tVVv+U+VTutFI5qK8/tgPswgrqZnmz3zu+s+DbZXl0l7Hkc5
cZ18cjwqrQF/dZKLChruV+OySpglMk+891lr7v0J9Ld0zJb343p0fuG
fkTnAMTgFNKibi58j3xXZS9pCztXxsvdTf59roZV371+7wxidcVTslGp
c7VPnXamxGxM9xfXUt39f+tP2CGYutrLadOYCdrN+LEwlm3PPFH
OqGslH3vmbIJarUnzn6759ex1I2suMt4OZtcmX1bO1dO/fDvLvrcn
bdoio96zJlYwCz9+LnyPe3XVxwt9TbgMZ2d3K7yKcu7z/dbiG+Z8zd
6ldP/USq+JKaY3zm09fj6vawzM6Bi41xIVgTlhrFk8eqb0l1O8/p510
Qe9Z83+N5DNheP3bGn0aGPrPCal11Hzayt8/vt9+1MVHt2sEZ+b5
XxiVWxjd+kvZc/KSbghX/P981WibI912Kl4CAo462SY9sreLrVLq3l0
3R9uX7Lmsz0Of9RQzutT9Y3Ep7rmiYauo2fuv5kQXuXNHp6fz9z7+
/v2VDDYf+NHuLWBtgmf13VZSf71NPe+eejs7cLbtDZ5Don7qXPbg
D2Td1MRRMmefP4tWnMS9J8Whw90bdrG9RZ/LmMfde//3fq2fe
ZRTYWAJTZZhRlXvxR5szHjZBruIB9fDYxdQ7jvOEOwPqsFZgB9WHx
Q6Pv2vetzLLZDJuLqCWtrjmUfj8Jlcs72Px0oUKALUcnS6136m5JQH
HTMro10MVTbtKZ+cOauQAZVqfB599vrbzdIAxnc33h4gHOK5Dcl
bI94eIBziiT2bWyXcARiPfATJVy/dapxuGeoBs3VJuoPUjn7955wJJ7
qQF2FB57dwzaWsmPtzTrW6X6zlEMVD//aT4B1FBqjFPyqustXmt
+s++KP7lo428z1KEkKR1xNd5rul1AxWH5PTfq9v5uF6golrPSR6tv/i
n81bJ93IOANDI87GaX4fmx8zxQdTP91tdKgEa+Xxe9tqjxFij/16ZYx
s08jk0P9fTCPpvp3yvSbhDdW8hvpbyn3+dJffbabI+wa1268+Lq7cF
Yr21r7WQecv9tcNAf5ekYv35kf8+9/iunO6oINChv8UHRp7/wMU5
+HuOIoe+pTP5flhZfRrqsnPFWgv9fVbgblW3eHp+VL4/ju+C8fN9ov
gr3pkT/Uao3pff843FPbzqG/OV66vflR2oOncodhLfJHlOk/8z1PIGr
67qvMr3Lw7VlfM33e3/ip0GrOtQy2JzaDoWOleDku/LdpbiX026Kt
bnqmpQ3efMyLf/vnkbaZjvQ11R3G/PNvcf9ZtxT0I341+9u8Td++/
PWVD7O+xv7wUu9zYQ/3TzjnzD+TOP0sHr6t/+WsbPKD90etG/w
w4ctfPRb51deG7RNt8fR/bp17Lpfyi+cJotUGDAVV0vzPcRx2dee9l
v/95zMwzFwHS+rjxzqxGbJuvPzO7391e4w7wKZklEGmh85jHAlBvj
7BBjkGcyG3+/nmRnHON0LS9pCHVvQ7025a8tyub5/pghK4U72wr
OLMfJ6PP6tIh2zXB/WSx+6ZlxhWurwYjXV7sxPWZkIwzWrc2qvpv

                    ~ 51 ~
         Savema Printer Programming Language (Rev.12)   03/08/2022

WHaCNqetVvmv/4z7Wat2wK9tsu2//Xbi382wMZy5T729R3SL42v
s5Rnay7fSct142XFN8hNN/72rwhVySQuHMb3l7GBuDO38g337BJ
ZPWzz/o9fLae698v3x3rxl2w+Coo2fGewbBKkb8/jG3xQe9Xj5seEjI
+MzXnT7gE4tulekxIxsxP6S/sha6/xOO6vDghMsrQKfLR63nHk0U7
pcX+SViYjHmh1zr627s02ZbR/zlFWDK8ZlDg32DhPvlJQ3j+HrCvbE
eZMWWe2h+1NEpNCM0+eH67xvld3Rcb+On9Qz3EYr5cnO1im26
8LUUT47qM0Fz0Vw1ebj+++vivWdmTY0Q7iMU8Agmuh5FT3t6xJ
+rfLdw6KLrRPW53+0bBQ/QKP6cy8dkJPurvA5v3i+6VtnQdovmXP
Ey7wjlPlz/fVvxaPue9543QomORhTy1Tid4rA7lqfJ98G77Ul1Avo7G
vHtGnWViB8kEObI95HDfZCCDJC6J8O6hO3snO7cQdndqgPq93yP
sp318/OzZ0xGuI/p8obaiHK/gz3hs/HeuhtTZtz++8mLGMZkGIcu/C
HjdOQfJ/ryI5wEDPp8vsHDfeOMgafsK6vTbfCMxtnJk3bke3crWj+T
Rbd9HNn5/rjBDxzECL34Qz7n9V9V+oP237ddtbN02/eTfW+mC6lx
/P6fqzfkmLdlVC6pAJPl+/al1HZ7cMbqRWuqRGfT7fDLN3imfL/q3q
XLC2k6Ou+LdOHPG7+2vG3htb343vleVjzb3WfhzlVUj/6m2+dvG9
wz4i+YtlXx8V2SfUA37Lzf8CcPYsBTop1TP/pUg3HHZ75eVBHucHM
TtdNLevGD5nuVVSTbfTtVxOznQz9kwC7n1EarRRvl2z/iu96/WvEJ
uVU+Z9Fo1WUuwov+9qwj39P+me+tb2webv33P9aTmdRcT7epy
yj85caJ+EPaVYYe4zNvs4KKf0zrbrsmd9Kdw50RTFqv2iVb83yvddX
YmAyDMwo/gkk7ao3qQ9t8f+u2t1si+IwZa8OAdN4fIn4YM9axFv
WhVb6fHJN5vteA+xSEO6OZsaZVj7sm+V5lTKbDvbwz1gAGpws/D
g28fr5fcpvWUZMO0o1J5/2NiB/HdFWubn2onO9VBtxbG3Or4tnt
XGLYIFpTMeJr5vsI69l/NeyGTUr3c5Eu/Gju2fCr5fv44T7dYXx8RmY
2iPjR3LAS1sn38cOdC922Stz2hw/rbiVSId+nCPdhN2xeJ5+Bzhtd+D
5uVSHP5vvOcH+dz96/Ht+qRPsQRjupewMavFAqbl75+mJr8yD//v
3393eQCBi8LCel876fdceGNUhGvapbAQrzfcD9skhraUG4HyXihzV
UlFUv+uR8105aEO5lRPzILg+0RiVeMv5++b74ylTIRsYv+mGpkCO7t
nTaffvh/vv4LVxDasRs9/Psw8F1zrfWpTzo81eLaRWXUwRVjN+RitS
z9nb4rqj+u2Rpx7B7LQbip9At6JoWcUj/3YB7UyMf1Kdj3QJe/fyfFh
+ekO+SvSlDxtWJePqYPt9lSlPCvRERz6uB5keOQ6Y0JdzHIeKDDTQ/
8t/bBqhtMqUp4d7B0XZkV/fRJ99GnD/z722X5rta3ppw70bEj6Zpu
HUuvmrri3WjfrdmAl9nIn4o28Wxf+XEEYpJvvM/9NwvIeIH8TXcny
+bogjmu746wtB/pEuW5udP0m2GdzBFuD8mHX9/zLN/Z6H/OAgF
caGdnfeJzNd/pzqZMo6Cjvzlna0MeeH+ODP+/mjZi//bqq+fP+lOH0
dBCdrnHZS1LEVTLDVq6lwlqBv0r5sUeVAdgWQfnIjvZqL5MEc1uQ
pcHPeLG5N6aL2QcJ+FlG8tezbwlbN8nk/i3vnKDdPt96vIi+nU7S3x
dIfLTnPM4nyI+NMk+9SkfF03OYXNyfc/s/ycnkRDhjNXuRTl0606Ot
Pk+x93Vx4i2fNI+WI3bA6T5fsj/XpIFSenM91zp83l/Iy1W5XybVvEf
Pn+uMeFkQLa/K1UmZScXeJaxJT5/rjZINo27fzOat16ElMB2t2LM6
NZ8/3PbcciK1biqfcDT3Kt+u30M+6EN3Pn++NOvVfVl69aLBkyYD1

                   ~ 52 ~
          Savema Printer Programming Language (Rev.12)   03/08/2022

pveTOgD+5zAT5vmep5cjOS+vlfUiV9ASip27LqCW1jgny/fFStJ2D/l
PDJ+G2r75TlDUVdV5acrqHBb3JayCT5furtS0fpK4MZYpSphEt4qv
UBjJHvj/W6+jG9t+8Ws9SsnRz8xaxKLuZTJPvj83auf0r7latJypTrnK3
RvHpDs1kpnx/1FhlLLVaz1WO7PFWVxsVcWqL2HCfxjJZvj+qLjQ2d
c2eruA4qv+aqVO3iG33bC/z5fuf6qvQTFGzJy0sil24oN4ULWKPO7
eaWfP9T9OFaK6t31OXC9WNsCDHXImvBT1mz/c/nZf9TL1FsM9oL
2eMcJPEyCmv0r5JyPdXt10ItIBnl89o5Hi9hIq6IS3f/1h+a4NYj3Hbr
FdLd8rM9z93Xr/lJo+X5Ck761XOMsn5/uaSBtB691r+m0+zZ73aW
MuN8v3V7A2glnuW/p2NVvPVwKZumu9Po1X3Dm5e4qzxgIE8d8
/3T5GJr5ThhuT7lnmzXrEC8r3c5emv7IAN8r2tpisoAGyQ7wCZ/nP
1BgDQhHwHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHy
CTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM
8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCT
fATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h
0gk3wHyCTfATLJd4BM8h0gk3wHyCTf4b9+fn5+fn6u3gqoQ77D4y
/TJTth5Ds8fn9/X/9X0JNBvsM/bykPs5Pv3J0xd1LJd3g8jMmQSL7D
AnFPAPnOrb3muEwnzI9rSmzbTr3f39/pYvGvzu/ZbK2DqU2f79OF
C4Ob8YhFRbNH4qvh8v2taT03T5MDRjNafr4ZKN8l+Oxe69LXUZ23
f1H6zG6cLH26ON+frfp5UnyHlt9in6/tvdZeS/Ax6qD2z8/PawXLq1
EMZZygvyzfd+bCxuZdlWjXKthRVb50zDv49x9U9n/gID+NAJen0wX
5/tp+tofXG22bBswa+U51F6Z8v3xfvHD61ovf+V5opPoJAfy5JOX/X
5+vWWwwe1qRlpbN/Chu4pLx5Ob9941x9sWv7t/Oiy8AMK/XKw
oOLfTUM1Ia9t+/RvnG6z+13ilyvKeCWTfttuTMmHuLg8T+aabMqG
dHvlX//WsD3m5UY6bt5zSSxdf02Rhgah1Srkm+7xmTKZswIz2BG
M2Hx+t+wf4xFjNnAB4tU75Jvq912w8NyEh24CYaRXzN66sbibw4/
v75V5k+mv3V7lDZjXZ9RcXjWnuu7ZV8bK0P3UjwtTubWswTGC04
mM7OkcOhOD5lqF7lavbf98x1O5rpE7UxilXpvGxPO9uerbt2/90s1
a9gO9921+dRreyEzJHmjOpVrlq7erOY9YfCvWzDWpwHqLJANxUj/
my+L15QPTot8vNle74UIFKtiD81PvP5bOLFHC8OdzkO3FCtgZqz4+
+vt6EWdNu/Lu8OcEN1LkoVD3O/ZffGer9HFycA4HF6oKak//45LP
P233sWIXDBnaG4ok6ekv77s/O+uMjqRnC3GGF/OxWYZUIbU9tTt
19f6eBBsTOZVj7Es3hBdU+4n6zfEpxgo026X+s8OUp1c6Y+HB6fW
ax/b4V9PtyHquIT2bjhYPuVb4yePb3NIOiwQ4bd58NuWLYzh/xW1
1f/lB3wr72zCWA0Zal49vrqmoJkt1AwQEVn579vXGLd8+Qz898Bvi
obpSnJ98XB2c/5M0cHKwU6QEUV7l997L6t6fHRYZfpAHsUdOFrrh
9Z8GxV+c6Y3GLNmA4ldp35kY/d4a7NvDl8QK6xACefPm9EEvHM
7nC+Hx1gaZE4d06xO//2pjZ27Mj7vOIRSCdsCodGaU6Nz2zfs3r+br
eR2xVJFpfceHx76hNvFmfT/flcR8RRpFinfH98W4Lm7d+/+iv1KpcE
wlrj11+0/XS6KhtQ5XPmbdivUwnO94q+ftHRd11o8d5G2umX7/8+
5dwiFXXHoAGy7czMCs/XXuu27/H1LaIcoEyF5/N9np0dPQMV4gD

                    ~ 53 ~
          Savema Printer Programming Language (Rev.12)   03/08/2022

77RyCrj9OLdwBWtsT3f858wU/Pz/PdN656Njf5bW3d4VdCwUYQc
3++55bb/TWAar4mt4Vrq8+di9IINxfOeABTdWcP/PUOdmHemzY
0fMhY1Pj+/rsyYoc9amowv1Nb//SKG3lIEzn6ORpB7ajtndsnfGZxS
+zJgHn1apFh26123NXdox2azDsmTO99o3OY6qosP7MU9kJ7POG
9dT2M6/9D2LUDuESzfvvZStKvmZ6izk81jzow36DYa3238sWtDq0
OvnnieHGaP7G+Z2IAe5pO6VP3d/0pjjcH0tHi7+boU5+KUCw7QAs
H585+qjVr1uz9icJDlCgyfz38688+mIA3pTn++uVzMVHbAtogAutjr8
fmn9iaiPAJbYGvU/Of+//LLGNW1emm0tTtsFlVz7eNHq0Voy5KhI3
tzoNstv6kQWKty3v+atcK/4u1jeHnplc9wMp0Dzfq5Sfe1lhXgP2qz
7z5Pkvb/ffTH0EapjvJ0cYdn7FaPVmZHZXddvdjo0m8Hbj3plT0rI3F
mt098nbHYue5lZFq3y3iNggBsn06uMYAzb4jTXIrtqY/VpsqjWILtck
37+uCfdsA9WH875u1Z62V/ZdxV25Pt5ORc9vp9YLg7sm3zdes+aS
8SKAea3FZp3139+uTpxJ2D3nBMUfDnAfC/33QyO5ny+WvwCdLY
b22fUjhTvAmJbz/dCSYc8XC3fyjDArCcos53tBnRbuAEOpc321Ubiv
zcPpuejN5+xAHbqb+Ly2dGaqqGmm9Fcn38/7GprnX3CGTL+h7UI
vqxKNKtIgd7dtaL2FDpyL6uf7npUcPl9j2Rkm0q6iljWE8RtO6y2s+/
mvpTD1keNsvv8l9dF1wfY8im/8Kku2xbm/ZRPGjt7xV9yaqq/Qt30
/ytTZ91XArzs7/331czsuSMA4vq4/0+5E7esnFy+GtbbIfkD7J8byVc
m6+b5/7ZeNbZranjZ/6Paxc5sD3MJiqtR5vvbn+gTdejczJuCM2wx
M50C+r/Xrz8xWlHQAjZztvwtogDF9WX9mcRbjoryRdICpnV1fDIAx
fRmfee2Vr/XQa82EPT9px2ARwNMo6xM8TsyMfr5lz5Hm6PPqth8
H/PyT4akwi49Xfa1gr3cS6VgwpnGf7yExGUT1h4ZzUuvlTGZcn6DV
/PfHwV2wePfT5zPpATYcjYs9R4VazxkdRLXnrz6KHqX99hbhzjjW1p
9ZHJY5WnWHzY65Oq0F2znLT6viVL7/7akznW6Bzpi+LoG3f+rwXG
J+CI9Lxmfm6rMfHXstu7pbsD2Ln6xx1mV/MrXr588UrLC69jlNZzJU
+eTBNw9IUm38vXW+7F81G+BW1rq/C/ev7h9D+Hvl6/TzP0VbCE
BN1dYn2HOnKwDdnMr35/yZ5/+6EwRgEA3nz+x/itPrW76uE7Dnq
wGoP3/mTOAWPyHz80NaHA/2nJQcnQ5U9/h0n9Ompneov93S8
XpW2vRh1m/bcObt5++3av2j3mb0rjVkzji1HkDBDavF3wVXMeqYJ
+yZ6avd2VpVtu5zpWEQc92OV1eL9aC+nvFMmrDXOpbvO8v1/Cp
rX8/RoI/PBYG3K/bOxailFa1tHSyrjM/81eOKY8qC/ipVBpQr9vtqjQ
GWXdqRzoyveb6/fc3l1ww1yz8SCuK1zfe3h9q8/ulCxc9pWnP5Lw
L41Gl8ZvHLxCJAOxsZXm19gqfPQRuL0gD0V+H+pj03JvxFvL48QEX
bXefy/vuhiRZ/S9N8duTf/kU3H6CWU/33sjheHKB/XWR4++1OAqg
i9YlXqb+LAuXXVz/X6NhfsVr30ze24cKvXlTrucxOfRr5evNdi5s8ucp
0x8Xtuncq39c+/dCCkWXfDt00XcvsJnb2S6aL12t9H+04me+P9cm
R+1dStD4Bw/pc8nDxr4vV+NBJ7efnwLaG+f7fj1hfgKm4po4T9NX7
bo1O5xeXTCm79bdKCQZ7WxbYLuISu5YIq5I124tHV2kAX4aZmrW
xWuPjs3C+/JUuNiPol+//PuvbIwJatIrPRy5U/4paDm3nhQmi/w7ja
57vn0MNHTrya8ZZ+gagtR75/ngZhdw+b91zmysAe/QYn9l4oOLO

                    ~ 54 ~
         Savema Printer Programming Language (Rev.12)   03/08/2022

bZLyAIfsXTig4vyZQ0Mxuw4+oh/gwwX5/jg+2n7o28X9DfW5YP71
ZuzFscfPee5lX/35mRtPVmjEPVyPeRJmfzE1mR+5c0T+Ta261WiW
TvXPPDqQRX9v56bxIVj2A7ePgs8/lX3mHnebsdo73//7caef2BfceJj
L2rWl1+7L85XOROnjUE07tX7k/nsd9x9gF18m9Ody4fO1yxzqiLy9
5u9/RTYDat5/f+vmVPmW7Uk47hqnOpWKERwe5qp1fXW7I99tu
YLnxmiKVCTfGcFl+f769dYCBaiuX74vTi14/O/Vp0OD6eIeYE1BVp9
9vvbaV25sytocLMtaASwq64hXHn9ffMTBxns/p5otfjjAoptcGikL6p
L++57Zb8/xme29X7aMQXxZjqbgenWtA/bReeVHTxw3noWysfBG
3atWrxbPYk/eN7T/TvK1/tnIy0aNsyXtlI+il7Wfz1mPax32gmuqQ81
2L57KXetGvpNO1v6hyqK/y29YLS6+m/Rq7+DUEr8nw2sx5b9eVt1
Z7W4eLoO4ah0F2QSPS/L9sd5t3z9t5tCiBWurmM2rIL/M64dbOZl
1Jfm+NjizNmPyTbsh9XajogCdnQ+0s/Mj/ywOUq91NhfHNKv0TKU
zwFPl53vsueL/2L84vbwG7qfa8nxnrq9+nULz9ZpqrdldADEuzvd/b
/7f6e17pkgWT8aQ8sAdVJw/cjbfn//9Njjz+dfFd23b3rYbJv5V82cK
VpvoZvt+i9eXffZFPh9gsDhT63VR0sUiOLR/ykrQDd43UbcpnR1/P7
Q4weu/H73/ImNOJNWV5ftjx+ji48glJTivespVfs7O1zawdvv14qetv
XFkImAibvJkHC0irs78yDWffaLP09XFX/XZ8DRCqlOpGESj/mvz52S
ujTyu3eV0fqQSYBZNRyZqPn91Y6Ty6Ij8yOvVkcfYOgXOV5vWw85
Nnq+9lubbf1p75c7XQzH5Tn8drilWHn//nKKwc12wzwb2NlbTc37
Y14vAG4sGi4kA4p6mus0Wqdx///ehmxMlj3bMC2bBF436PdwAA
AMjSURBVLTPr/PqdpILwIaeUwGb5Pu/jz6y/kzxYjW18n3t6+Q1UE
X/Sd4N8/2x0m3f/4Anc2mAAFfdvtNj/vtfNH8ds/776yz3MQF8dW
2gte2///dr9k2XfKzsDp13YC4jdFXb9t+fvt4Ivv2C/bMq4QwzZzhph
Fh/6tR//5+v3PfcPs0MmMVQsf50Qb7/++Ljy5ABDGXMWH+6LN//
uwWlgzYAX10ecRe6Pt+f5DiMY5xkoNhA+f5G3NPTnsm7R29mhm
uprwCZ/nP1BgDQhHwHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM
8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCT
fATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h
0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfA
TLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0g
k3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATL
Jd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3
wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd
4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3w
HyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4B
M8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHy
CTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM
8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCT
fATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h
0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfA

                   ~ 55 ~
          Savema Printer Programming Language (Rev.12)   03/08/2022

TLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM/x/3
GNqpj38qYQAAAABJRU5ErkJggg==</ImageData>

<OriginalImageData>iVBORw0KGgoAAAANSUhEUgAAAfUAAALu
CAIAAAA40xiDAAAACXBIWXMAAA7DAAAOwwHHb6hkAAAgAElE
QVR4nO3d3ZqrqqIt0NT6zvu/cp2LGjsrK1GjCAjd1q7mHJUfI9BFRP
z5/f19ABDnP1dvAABNyHeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8g
k3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJ
N8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXe
ATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3
wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN
8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeA
TPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3w
EyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B
8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPI
dIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyy
XeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8g
k3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJ
N8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXe
ATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3
wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN
8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeA
TPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3w
EyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B
8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPI
dIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyyXeATPIdIJN8B8gk3wEyy
XeATPIdIJN8B8j0/67egDn8/Pxs/PX393fxla//3mIDKn4+kOfnzhmx
ndqLLt9dh7b58q2FAnoztdwu33fm4/i7ZcaDE4P4qzxD1Yefn39Zd
LRiD/UrRpOc7xLwsW8n5P3qO9so8XkLuqAtP2b+vbXk5Pvgaf7sng
yi0XUCrrJW/2MKtyziX/3+/o7WDFub9deeL+w3VfZD9a36NGl5UV
FkD32NNnXGHPneoYz/vO2Nbt9b1xRlSoFG4+aT1vN2YlrQoPnev8
L9nbt1/tLOxixrFj1rY61Si6/e7czbcAbKd/Wvs3GKnro0pQ6maD5X5
nt2LWy6YxvtuimqLG+GakfPKjTUVjU1cqvpmu9JRT5modbaw2P+u
hgnZ3FkHN2T0uDVUG2neb5nlOJQZXZIxf0/706YWosWNHhRCo1
amuT77MUzQsG0VlxGd9g5I6gy3bvKllxu9jz5c0lxVMv3qcsgpiWU
OVN2n2ur3XxnFqvSguJ3/tQ589StmM7m+9S7O74xFKhboPbwG7
u3hUlTqEPxleT7pHvzoT0clHEd70J2YGei6c2BfLfvbqtp0ceUjr00lBn
zqv6dydn5rlXU1boOTFRe1kUZ34yR9aj7XKDIfNcwmrqkJlxbpp1/s
gpc3T3ja2++T7F3tIrOBqkVa6vCTdd3UYFbG6Sgv6o2rTEm37WNq
4xfNwan6vY3eKWtVSWmf762tnG5Gy45Uovae5WyZwFOp1++t0

                    ~ 56 ~
         Savema Printer Programming Language (Rev.12)   03/08/2022

gBzWMod1hjuSK193ItauznHX8X6jQ+0+K5GZrHsC6v1iNTbwfUc2
G+no9E7jr+XutoqYVMQcp/UnWHVTGavl7h//pdU+Z7FVrIRMapN
pdTb6cwSI2tVVv+U+VTutFI5qK8/tgPswgrqZnmz3zu+s+DbZXl0l7
Hkc5cZ18cjwqrQF/dZKLChruV+OySpglMk+891lr7v0J9Ld0zJb343p
0fuGfkTnAMTgFNKibi58j3xXZS9pCztXxsvdTf59roZV371+7wxidcV
TslGpc7VPnXamxGxM9xfXUt39f+tP2CGYutrLadOYCdrN+LEwlm3
PPFHOqGslH3vmbIJarUnzn6759ex1I2suMt4OZtcmX1bO1dO/fDv
Lvrcnbdoio96zJlYwCz9+LnyPe3XVxwt9TbgMZ2d3K7yKcu7z/dbiG
+Z8zd6ldP/USq+JKaY3zm09fj6vawzM6Bi41xIVgTlhrFk8eqb0l1O8
/p510Qe9Z83+N5DNheP3bGn0aGPrPCal11Hzayt8/vt9+1MVHt2s
EZ+b5XxiVWxjd+kvZc/KSbghX/P981WibI912Kl4CAo462SY9sreLrV
Lq3l03R9uX7Lmsz0Of9RQzutT9Y3Ep7rmiYauo2fuv5kQXuXNHp6f
z9z7+/v2VDDYf+NHuLWBtgmf13VZSf71NPe+eejs7cLbtDZ5Don7q
XPbgD2Td1MRRMmefP4tWnMS9J8Whw90bdrG9RZ/LmMfde//3
fq2feZRTYWAJTZZhRlXvxR5szHjZBruIB9fDYxdQ7jvOEOwPqsFZgB
9WHxQ6Pv2vetzLLZDJuLqCWtrjmUfj8Jlcs72Px0oUKALUcnS6136
m5JQHHTMro10MVTbtKZ+cOauQAZVqfB599vrbzdIAxnc33h4gH
OK5DclbI94eIBziiT2bWyXcARiPfATJVy/dapxuGeoBs3VJuoPUjn79
55wJJ7qQF2FB57dwzaWsmPtzTrW6X6zlEMVD//aT4B1FBqjFPyq
ustXmt+s++KP7lo428z1KEkKR1xNd5rul1AxWH5PTfq9v5uF6golrP
SR6tv/in81bJ93IOANDI87GaX4fmx8zxQdTP91tdKgEa+Xxe9tqjxFij
/16ZYxs08jk0P9fTCPpvp3yvSbhDdW8hvpbyn3+dJffbabI+wa1268
+Lq7cFYr21r7WQecv9tcNAf5ekYv35kf8+9/iunO6oINChv8UHRp7
/wMU5+HuOIoe+pTP5flhZfRrqsnPFWgv9fVbgblW3eHp+VL4/ju+
C8fN9ovgr3pkT/Uao3pff843FPbzqG/OV66vflR2oOncodhLfJHlOk/
8z1PIGr67qvMr3Lw7VlfM33e3/ip0GrOtQy2JzaDoWOleDku/Ldpb
iX026KtbnqmpQ3efMyLf/vnkbaZjvQ11R3G/PNvcf9ZtxT0I341+9u
8Td++/PWVD7O+xv7wUu9zYQ/3TzjnzD+TOP0sHr6t/+WsbPKD90
etG/ww4ctfPRb51deG7RNt8fR/bp17Lpfyi+cJotUGDAVV0vzPcRx2
dee9lv/95zMwzFwHS+rjxzqxGbJuvPzO7391e4w7wKZklEGmh85j
HAlBvj7BBjkGcyG3+/nmRnHON0LS9pCHVvQ7025a8tyub5/pghK4
U72wrOLMfJ6PP6tIh2zXB/WSx+6ZlxhWurwYjXV7sxPWZkIwzWrc
2qvpvWHaCNqetVvmv/4z7Wat2wK9tsu2//Xbi382wMZy5T729R3
SL42vs5Rnay7fSct142XFN8hNN/72rwhVySQuHMb3l7GBuDO38g
337BJZPWzz/o9fLae698v3x3rxl2w+Coo2fGewbBKkb8/jG3xQe9Xj
5seEjI+MzXnT7gE4tulekxIxsxP6S/sha6/xOO6vDghMsrQKfLR63nH
k0U7pcX+SViYjHmh1zr627s02ZbR/zlFWDK8ZlDg32DhPvlJQ3j+Hr
CvbEeZMWWe2h+1NEpNCM0+eH67xvld3Rcb+On9Qz3EYr5cnO1
im268LUUT47qM0Fz0Vw1ebj+++vivWdmTY0Q7iMU8Agmuh5FT
3t6xJ+rfLdw6KLrRPW53+0bBQ/QKP6cy8dkJPurvA5v3i+6VtnQdov

                   ~ 57 ~
          Savema Printer Programming Language (Rev.12)   03/08/2022

mXPEy7wjlPlz/fVvxaPue9543QomORhTy1Tid4rA7lqfJ98G77Ul1A
vo7GvHtGnWViB8kEObI95HDfZCCDJC6J8O6hO3snO7cQdndqgPq
93yPsp318/OzZ0xGuI/p8obaiHK/gz3hs/HeuhtTZtz++8mLGMZkGI
cu/CHjdOQfJ/ryI5wEDPp8vsHDfeOMgafsK6vTbfCMxtnJk3bke3cr
Wj+TRbd9HNn5/rjBDxzECL34Qz7n9V9V+oP237ddtbN02/eTfW+
mC6lx/P6fqzfkmLdlVC6pAJPl+/al1HZ7cMbqRWuqRGfT7fDLN3im
fL/q3qXLC2k6Ou+LdOHPG7+2vG3htb343vleVjzb3WfhzlVUj/6m2
+dvG9wz4i+YtlXx8V2SfUA37Lzf8CcPYsBTop1TP/pUg3HHZ75eVB
HucHMTtdNLevGD5nuVVSTbfTtVxOznQz9kwC7n1EarRRvl2z/iu9
6/WvEJuVU+Z9Fo1WUuwov+9qwj39P+me+tb2webv33P9aTmdR
cT7epyyj85caJ+EPaVYYe4zNvs4KKf0zrbrsmd9Kdw50RTFqv2iVb8
3yvddXYmAyDMwo/gkk7ao3qQ9t8f+u2t1si+IwZa8OAdN4fIn4YM
9axFvWhVb6fHJN5vteA+xSEO6OZsaZVj7sm+V5lTKbDvbwz1gAGp
ws/Dg28fr5fcpvWUZMO0o1J5/2NiB/HdFWubn2onO9VBtxbG3Or
4tntXGLYIFpTMeJr5vsI69l/NeyGTUr3c5Eu/Gju2fCr5fv44T7dYXx8
RmY2iPjR3LAS1sn38cOdC922Stz2hw/rbiVSId+nCPdhN2xeJ5+Bzh
td+D5uVSHP5vvOcH+dz96/Ht+qRPsQRjupewMavFAqbl75+mJr8y
D//v3393eQCBi8LCel876fdceGNUhGvapbAQrzfcD9skhraUG4HyX
ihzVUlFUv+uR8105aEO5lRPzILg+0RiVeMv5++b74ylTIRsYv+mGpk
CO7tnTaffvh/vv4LVxDasRs9/Psw8F1zrfWpTzo81eLaRWXUwRVjN
+RitSz9nb4rqj+u2Rpx7B7LQbip9At6JoWcUj/3YB7UyMf1Kdj3QJe/
fyfFh+ekO+SvSlDxtWJePqYPt9lSlPCvRERz6uB5keOQ6Y0JdzHIeKD
DTQ/8t/bBqhtMqUp4d7B0XZkV/fRJ99GnD/z722X5rta3ppw70bEj
6ZpuHUuvmrri3WjfrdmAl9nIn4o28Wxf+XEEYpJvvM/9NwvIeIH8T
Xcny+bogjmu746wtB/pEuW5udP0m2GdzBFuD8mHX9/zLN/Z6H/
OAgFcaGdnfeJzNd/pzqZMo6Cjvzlna0MeeH+ODP+/mjZi//bqq+fP
+lOH0dBCdrnHZS1LEVTLDVq6lwlqBv0r5sUeVAdgWQfnIjvZqL5M
Ec1uQpcHPeLG5N6aL2QcJ+FlG8tezbwlbN8nk/i3vnKDdPt96vIi+n
U7S3xdIfLTnPM4nyI+NMk+9SkfF03OYXNyfc/s/ycnkRDhjNXuRTl0
606OtPk+x93Vx4i2fNI+WI3bA6T5fsj/XpIFSenM91zp83l/Iy1W5Xy
bVvEfPn+uMeFkQLa/K1UmZScXeJaxJT5/rjZINo27fzOat16ElMB2t
2LM6NZ8/3PbcciK1biqfcDT3Kt+u30M+6EN3Pn++NOvVfVl69aLBk
yYD1pveTOgD+5zAT5vmep5cjOS+vlfUiV9ASip27LqCW1jgny/fFStJ
2D/lPDJ+G2r75TlDUVdV5acrqHBb3JayCT5furtS0fpK4MZYpSphEt
4qvUBjJHvj/W6+jG9t+8Ws9SsnRz8xaxKLuZTJPvj83auf0r7latJypTr
nK3RvHpDs1kpnx/1FhlLLVaz1WO7PFWVxsVcWqL2HCfxjJZvj+qLj
Q2dc2eruA4qv+aqVO3iG33bC/z5fuf6qvQTFGzJy0sil24oN4ULWK
PO7eaWfP9T9OFaK6t31OXC9WNsCDHXImvBT1mz/c/nZf9TL1Fs
M9oL2eMcJPEyCmv0r5JyPdXt10ItIBnl89o5Hi9hIq6IS3f/1h+a4NYj
3HbrFdLd8rM9z93Xr/lJo+X5Ck761XOMsn5/uaSBtB691r+m0+zZ7

                    ~ 58 ~
         Savema Printer Programming Language (Rev.12)   03/08/2022

3aWMuN8v3V7A2glnuW/p2NVvPVwKZumu9Po1X3Dm5e4qzxgI
E8d8/3T5GJr5ThhuT7lnmzXrEC8r3c5emv7IAN8r2tpisoAGyQ7wC
Z/nP1BgDQhHwHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk
3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJ
d4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3
wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd
4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTf4b9+fn5+fn6u3gqo
Q77D4y/TJTth5Ds8fn9/X/9X0JNBvsM/bykPs5Pv3J0xd1LJd3g8jM
mQSL7DAnFPAPnOrb3muEwnzI9rSmzbTr3f39/pYvGvzu/ZbK2Dq
U2f79OFC4Ob8YhFRbNH4qvh8v2taT03T5MDRjNafr4ZKN8l+Oxe6
9LXUZ23f1H6zG6cLH26ON+frfp5UnyHlt9in6/tvdZeS/Ax6qD2z8/P
awXLq1EMZZygvyzfd+bCxuZdlWjXKthRVb50zDv49x9U9n/gID+N
AJen0wX5/tp+tofXG22bBswa+U51F6Z8v3xfvHD61ovf+V5opPoJA
fy5JOX/X5+vWWwwe1qRlpbN/Chu4pLx5Ob9941x9sWv7t/Oiy8A
MK/XKwoOLfTUM1Ia9t+/RvnG6z+13ilyvKeCWTfttuTMmHuLg8T+
aabMqGdHvlX//WsD3m5UY6bt5zSSxdf02Rhgah1Srkm+7xmTKZs
wIz2BGM2Hx+t+wf4xFjNnAB4tU75Jvq912w8NyEh24CYaRXzN66s
bibw4/v75V5k+mv3V7lDZjXZ9RcXjWnuu7ZV8bK0P3UjwtTubWs
wTGC04mM7OkcOhOD5lqF7lavbf98x1O5rpE7UxilXpvGxPO9uerb
t2/90s1a9gO9921+dRreyEzJHmjOpVrlq7erOY9YfCvWzDWpwHqL
JANxUj/my+L15QPTot8vNle74UIFKtiD81PvP5bOLFHC8OdzkO3FC
tgZqz4++vt6EWdNu/Lu8OcEN1LkoVD3O/ZffGer9HFycA4HF6oKa
k//45LPP233sWIXDBnaG4ok6ekv77s/O+uMjqRnC3GGF/OxWYZU
IbU9tTt19f6eBBsTOZVj7Es3hBdU+4n6zfEpxgo026X+s8OUp1c6Y+
HB6fWax/b4V9PtyHquIT2bjhYPuVb4yePb3NIOiwQ4bd58NuWLY
zh/xW11f/lB3wr72zCWA0Zal49vrqmoJkt1AwQEVn579vXGLd8+Q
z898BviobpSnJ98XB2c/5M0cHKwU6QEUV7l997L6t6fHRYZfpAHs
UdOFrrh9Z8GxV+c6Y3GLNmA4ldp35kY/d4a7NvDl8QK6xACefPm
9EEvHM7nC+Hx1gaZE4d06xO//2pjZ27Mj7vOIRSCdsCodGaU6Nz2
zfs3r+breR2xVJFpfceHx76hNvFmfT/flcR8RRpFinfH98W4Lm7d+/+
iv1KpcEwlrj11+0/XS6KhtQ5XPmbdivUwnO94q+ftHRd11o8d5G2u
mX7/8+5dwiFXXHoAGy7czMCs/XXuu27/H1LaIcoEyF5/N9np0dPQ
MV4gD77RyCrj9OLdwBWtsT3f858wU/Pz/PdN656Njf5bW3d4Vd
CwUYQc3++55bb/TWAar4mt4Vrq8+di9IINxfOeABTdWcP/PUOd
mHemzY0fMhY1Pj+/rsyYoc9amowv1Nb//SKG3lIEzn6ORpB7ajtnd
snfGZxS+zJgHn1apFh26123NXdox2azDsmTO99o3OY6qosP7MU9
kJ7POG9dT2M6/9D2LUDuESzfvvZStKvmZ6izk81jzow36DYa3238s
WtDq0OvnnieHGaP7G+Z2IAe5pO6VP3d/0pjjcH0tHi7+boU5+KUC
w7QAsH585+qjVr1uz9icJDlCgyfz38688+mIA3pTn++uVzMVHbAto
gAutjr8fmn9iaiPAJbYGvU/Of+//LLGNW1emm0tTtsFlVz7eNHq0V

                   ~ 59 ~
          Savema Printer Programming Language (Rev.12)   03/08/2022

oy5KhI3tzoNstv6kQWKty3v+atcK/4u1jeHnplc9wMp0Dzfq5Sfe1lh
XgP2qz7z5Pkvb/ffTH0EapjvJ0cYdn7FaPVmZHZXddvdjo0m8Hbj3p
lT0rI3Fmt098nbHYue5lZFq3y3iNggBsn06uMYAzb4jTXIrtqY/Vpsqj
WILtck37+uCfdsA9WH875u1Z62V/ZdxV25Pt5ORc9vp9YLg7sm3z
des+aS8SKAea3FZp3139+uTpxJ2D3nBMUfDnAfC/33QyO5ny+Wv
wCdLYb22fUjhTvAmJbz/dCSYc8XC3fyjDArCcos53tBnRbuAEOpc3
21UbivzcPpuejN5+xAHbqb+Ly2dGaqqGmm9Fcn38/7GprnX3CGT
L+h7UIvqxKNKtIgd7dtaL2FDpyL6uf7npUcPl9j2Rkm0q6iljWE8RtO
6y2s+/mvpTD1keNsvv8l9dF1wfY8im/8Kku2xbm/ZRPGjt7xV9yaq
q/Qt30/ytTZ91XArzs7/331czsuSMA4vq4/0+5E7esnFy+GtbbIfkD7
J8byVcm6+b5/7ZeNbZranjZ/6Paxc5sD3MJiqtR5vvbn+gTdejczJuC
M2wxM50C+r/Xrz8xWlHQAjZztvwtogDF9WX9mcRbjoryRdICpnV
1fDIAxfRmfee2Vr/XQa82EPT9px2ARwNMo6xM8TsyMfr5lz5Hm6
PPqth8H/PyT4akwi49Xfa1gr3cS6VgwpnGf7yExGUT1h4ZzUuvlTG
Zcn6DV/PfHwV2wePfT5zPpATYcjYs9R4VazxkdRLXnrz6KHqX99hb
hzjjW1p9ZHJY5WnWHzY65Oq0F2znLT6viVL7/7akznW6Bzpi+LoG
3f+rwXGJ+CI9Lxmfm6rMfHXstu7pbsD2Ln6xx1mV/MrXr588UrLC
69jlNZzJU+eTBNw9IUm38vXW+7F81G+BW1rq/C/ev7h9D+Hvl6/T
zP0VbCEBN1dYn2HOnKwDdnMr35/yZ5/+6EwRgEA3nz+x/itPrW7
6uE7DnqwGoP3/mTOAWPyHz80NaHA/2nJQcnQ5U9/h0n9Ompn
eov93S8XpW2vRh1m/bcObt5++3av2j3mb0rjVkzji1HkDBDavF3w
VXMeqYJ+yZ6avd2VpVtu5zpWEQc92OV1eL9aC+nvFMmrDXOpb
vO8v1/CprX8/RoI/PBYG3K/bOxailFa1tHSyrjM/81eOKY8qC/ipVBp
Qr9vtqjQGWXdqRzoyveb6/fc3l1ww1yz8SCuK1zfe3h9q8/ulCxc9p
WnP5LwL41Gl8ZvHLxCJAOxsZXm19gqfPQRuL0gD0V+H+pj03JvxF
vL48QEXbXefy/vuhiRZ/S9N8duTf/kU3H6CWU/33sjheHKB/XWR4
++1OAqgi9YlXqb+LAuXXVz/X6NhfsVr30ze24cKvXlTrucxOfRr5evN
di5s8ucp0x8Xtuncq39c+/dCCkWXfDt00XcvsJnb2S6aL12t9H+04m
e+P9cmR+1dStD4Bw/pc8nDxr4vV+NBJ7efnwLaG+f7fj1hfgKm4po
4T9NX7bo1O5xeXTCm79bdKCQZ7WxbYLuISu5YIq5I124tHV2kAX
4aZmrWxWuPjs3C+/JUuNiPol+//PuvbIwJatIrPRy5U/4paDm3nhQ
mi/w7ja57vn0MNHTrya8ZZ+gagtR75/ngZhdw+b91zmysAe/QYn9
l4oOLObZLyAIfsXTig4vyZQ0Mxuw4+oh/gwwX5/jg+2n7o28X9Df
W5YP71ZuzFscfPee5lX/35mRtPVmjEPVyPeRJmfzE1mR+5c0T+Ta
261WiWTvXPPDqQRX9v56bxIVj2A7ePgs8/lX3mHnebsdo73//7ca
ef2BfceJjL2rWl1+7L85XOROnjUE07tX7k/nsd9x9gF18m9Ody4fO1
yxzqiLy95u9/RTYDat5/f+vmVPmW7Uk47hqnOpWKERwe5qp1fX
W7I99tuYLnxmiKVCTfGcFl+f769dYCBaiuX74vTi14/O/Vp0OD6eIe
YE1BVp99vvbaV25sytocLMtaASwq64hXHn9ffMTBxns/p5otfjjAop
tcGikL6pL++57Zb8/xme29X7aMQXxZjqbgenWtA/bReeVHTxw3n

                    ~ 60 ~
         Savema Printer Programming Language (Rev.12)   03/08/2022

oWysfBG3atWrxbPYk/eN7T/TvK1/tnIy0aNsyXtlI+il7Wfz1mPax32
gmuqQ812L57KXetGvpNO1v6hyqK/y29YLS6+m/Rq7+DUEr8nw2
sx5b9eVt1Z7W4eLoO4ah0F2QSPS/L9sd5t3z9t5tCiBWurmM2rIL/
M64dbOZl1Jfm+NjizNmPyTbsh9XajogCdnQ+0s/Mj/ywOUq91Nhf
HNKv0TKUzwFPl53vsueL/2L84vbwG7qfa8nxnrq9+nULz9Zpqrdld
ADEuzvd/b/7f6e17pkgWT8aQ8sAdVJw/cjbfn//9Njjz+dfFd23b3rY
bJv5V82cKVpvoZvt+i9eXffZFPh9gsDhT63VR0sUiOLR/ykrQDd43U
bcpnR1/P7Q4weu/H73/ImNOJNWV5ftjx+ji48glJTivespVfs7O1za
wdvv14qetvXFkImAibvJkHC0irs78yDWffaLP09XFX/XZ8DRCqlOpG
ESj/mvz52SujTyu3eV0fqQSYBZNRyZqPn91Y6Ty6Ij8yOvVkcfYOgX
OV5vWw85Nnq+9lubbf1p75c7XQzH5Tn8drilWHn//nKKwc12wz
wb2NlbTc37Y14vAG4sGi4kA4p6mus0Wqdx///ehmxMlj3bMC2bB
F436PdwAAAMjSURBVLTPr/PqdpILwIaeUwGb5Pu/jz6y/kzxYjW1
8n3t6+Q1UEX/Sd4N8/2x0m3f/4Anc2mAAFfdvtNj/vtfNH8ds/776
yz3MQF8dW2gte2///dr9k2XfKzsDp13YC4jdFXb9t+fvt4Ivv2C/bM
q4QwzZzhphFh/6tR//5+v3PfcPs0MmMVQsf50Qb7/++Ljy5ABDG
XMWH+6LN//uwWlgzYAX10ecRe6Pt+f5DiMY5xkoNhA+f5G3NPT
nsm7R29mhmuprwCZ/nP1BgDQhHwHyCTfATLJd4BM8h0gk3wH
yCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4B
M8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHy
CTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM
8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCT
fATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h
0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfA
TLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0g
k3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATL
Jd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3
wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd
4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3w
HyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4B
M8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHy
CTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM
8h0gk3wHyCTfATLJd4BM8h0gk3wHyCTfATLJd4BM8h0gk3wHyCT
fATLJd4BM/x/3GNqpj38qYQAAAABJRU5ErkJggg==</OriginalIma
geData>
  <Inverted>False</Inverted>
  <IsFiltered>False</IsFiltered>
  <Mirror>False</Mirror>
 </Content>
 <Font>

                   ~ 61 ~
                              Savema Printer Programming Language (Rev.12)        03/08/2022

                  <Name>Arial</Name>
                  <Size>18</Size>
                  <Style>Regular</Style>
                 </Font>
                 </Object>
                    <Dates/>
                   </Template>}^ --- This template has only one logo


3.1.3.7       Shape

Shape object have two different properties which is shown in below,

  1- ShapeType: This item specifies shape type. This can be Rectangle, Ellipse,
     FilledRectangle and FilledEllipse.
  2- LineThickness : This item adjust thickness of line. It is measured in pixel.Forexample;
     12 pixels = 1mm.Line thickness cannot be bigger than widht and height value.

   Example      ~SPLTDS{<Template>
                      <General>
                 <MachineType>53*70 I</MachineType>
                 <Name>temp1_53.rox</Name>
                 <Width>640</Width>
                 <Height>912</Height>
                 <ZIndex>-1000</ZIndex>
                 <SaveImages>False</SaveImages>
                 <DataSourcesInfo/>
                 </General>
                 <DataSources/>
                 <Object>
                 <ObjectType>Shape</ObjectType>
                 <NameID>Draw07</NameID>
                 <Name>shape1</Name>
                 <X>172</X>
                 <Y>192</Y>
                 <W>150</W>
                 <H>6</H>
                 <ZIndex>0</ZIndex>
                 <Rotate>0</Rotate>
                 <Hidden>False</Hidden>
                 <Content>

                                         ~ 62 ~
                             Savema Printer Programming Language (Rev.12)        03/08/2022

                  <Source>Internal</Source>
                  <ShapeType>Line</ShapeType>
                  <LineThickness>5</LineThickness>
                 </Content>
                 <Font>
                  <Name>Arial</Name>
                  <Size>18</Size>
                  <Style>Regular</Style>
                 </Font>
                 </Object>
                    <Dates/>
                   </Template>}^ --- This template has only one shape




3.1.3.8       Barcode

Barcode object have various properties which is shown in below,

  1- Source: This item specify barcode value source. it can be Internal and External.
         a- Internal : This is default selection for barcode. Barcode value is identified
             from PC when creating a template in this mode.
         b- External : Barcode object gets value from RS-232 or Ethernet interface.
         c- Counter : Barcode object value is increasing one by one. Barcode values must
             be numeric in this type.
         d- Database : Barcode object value is changing one by one with data file(.csv file)
             content. Csv file must be stored in controller.



  2- BarcodeType : It stores barcode type. We support many barcodes. These are;
         ➢ Codabar
         ➢ Code 11
         ➢ Code 128
         ➢ Code 32
         ➢ Code 39
         ➢ Code 93
         ➢ Deutsche Post Identcode
         ➢ Deutsche Post Leitcode
         ➢ EAN-13
         ➢ EAN-8

                                        ~ 63 ~
                           Savema Printer Programming Language (Rev.12)        03/08/2022

        ➢   EAN-99
        ➢   EAN-Velocity
        ➢   FedEx Ground 96
        ➢   Industrial 2 of 5
        ➢   Interleaved 2 o5
        ➢   ISBN
        ➢   ISMN
        ➢   ISSN
        ➢   ITF-14
        ➢   JAN-13
        ➢   JAN-8
        ➢   MSI
        ➢   OPC
        ➢   PharmaCode
        ➢   PLANET
        ➢   POSTNET
        ➢   PZN
        ➢   SCC-14
        ➢   SCC-18
        ➢   Telepen
        ➢   UCC/EAN-128
        ➢   GS1-128
        ➢   UPC-A
        ➢   UPC-E
        ➢   GS1-Databar Omnidirectional
        ➢   GS1-Databar Omnidirectional Stacked
        ➢   GS1-Databar Truncated
        ➢   GS1-Databar Limited
        ➢   GS1-Databar Stacked
        ➢   GS1-Databar Expanded
        ➢   GS1-Databar Expanded Stacked

3- BarcodeValue: It stores barcode value.Barcode value must be suitable for barcode
   type. For example 8691234567890 value is suitable for EAN-13 but 8691234567891 is
   not suitable.

4- AddCheckSum : Specifieswhether checksum must be generated and attached to the
   value to encode.This property is using for some barcodes.(Code 128, Code 39...etc) . It
   can be True or False.
       a- True : Adding checksum value to barcode
       b- False : Do not add cheksum value to barcode

                                      ~ 64 ~
                            Savema Printer Programming Language (Rev.12)          03/08/2022



5- BarHeight : Specifiesthe bar’s height of the barcode. It values are changing between
   0.1-5.0 values. It's measured in inches. Please see note at the end of barcode object.
   It shows inch calculating.

6- BarRatio : Specifiesthe wide bar’s width compared to the narrow bar’s width. In
   barcode terminology this is N value. It is changing between 1-20.

7- BarWidth : Specifies the narrow bar’s width of the barcode. In barcode terminology
   this is X value. It's measured in inches. Please see note at the end of barcode object. It
   shows inch calculating.

8- BearerBarStyle : Specifiesthe bearer bar’s type that must be drawn with the barcode
   image. Bearer bar is only available for 2 of 5, Code 128 and UCC/EAN-128 barcodes. It
   can beNone, FrameandHorizontal Rules. These are;
       a- None : it doesn’t add any line around the barcode
       b- Frame : it adds frame to around the barcode
       c- Horizontal Rules : it adds lines top and bottom of barcode lines.

9- BearerBarWidth :Specifies the bearer bar’s width. It's measured in inches. Please see
   note at the end of barcode object. It shows inch calculating.

10- BorderWidth :Specifiesthe barcode image border's width. Border property is drawing
    frame around barcode. Default it is 0. If it is 0, cannot see any frame around barcode.

11- CodabarStartChar :Specifiesthe start character for Codabar symbology. Possible
    values are: A, B, C or D.

12- CodabarStopChar : Specifiesthe stop character for Codabar symbology. Possible
    values are: A, B, C or D.

13- Code128Charset : Specifies the characters set to use in the Code 128 symbology.
    Possible values are: Auto, A, B or C.

14- CodeAlignment : Specifies location of code according to barcode. It can be Below Left,
    Below Center, Below Right, Above Left, Above Center, Above Right.

15- DisplayCode : Specifieswhether the value to encode must be displayed in the barcode
    image. It can be True or False.
        a- True : Barcode value appears with barcode value.
        b- False : Barcode value code doesn’t appear.

                                       ~ 65 ~
                            Savema Printer Programming Language (Rev.12)        03/08/2022

16- DisplayChecksum : Specifies whether checksum is printed or not.It can be True or
    False.
        c- True : Checksum appears with barcode value.
        d- False : Checksum value code doesn’t appear. This value is default.

17- DisplayLightMarginIndicator : Specifieswhether light margin indicators must be
    displayed in the barcode image. Only available for EAN/UPC Symbologies. It can be
    True or False.
        a- True : Indicator appears with barcode..
        b- False : Indicator code doesn’t appear.

18- DisplayStartStopChar : Specifieswhether start and stop characters must be displayed
    in the barcode image. İt is usign with some barcodes(Codabar, Code39..etc). It can be
    True or False.
         a- True : Start Stop Charappears with barcode value.
         b- False : Start Stop Char doesn’t appear.

19- EanUpcSupplementType : Specifies whether use EAN or UPC with supplement
    barcode or use single type barcode. It is using with EAN and UPC barcodes(EAN13,
    EAN8, UPC-A, UPC-E). It can be None, Digits2 (Addon 2) or Digits5 (Addon 5). Addon
    barcode values doesn’t read by barcode scanner.
        a- None : Only single barcode appears
        b- Digits2 : Barcode appears with two digits addon barcode
        c- Digits5: Barcode appears with five digits addon barcode

20- EanUpcSupplementCode : Specifies value of supplement barcode. If supplement type
    is Digits2, this value must be 2 digits. If supplement type is Digits5, value must be 5
    digits. Code value must be numerical.

21- EanUpcSupplementSeparator : Specifies distance between main barcode bars and
    addon(supplement) barcode bars. It's measured in inches. Please see note at the end
    of barcode object. It shows inch calculating.

22- EanUpcSupplementMargin : Specifies distance between top of addon barcode bars
    and addon value. It's measured in inches. Please see note at the end of barcode
    object. It shows inch calculating.

23- SegmentCount : Specifies segment count of GS1-Databar Expanded and GS1-Databar
    Expanded Stacked. This is numerical value and default it is 6. It can be
    2,4,6,8,10,12,14,16,18 and 20.



                                       ~ 66 ~
                            Savema Printer Programming Language (Rev.12)          03/08/2022

24- QuietZoneWidth : Specifies the right and left side gap of the barcode. It values are
    changing between 0.01 - 5.0 values. It's measured in inches. Please see note at the
    end of barcode object. It shows inch calculating.

25- PharmaCodeBarSpacing: Specifies gaps between bars of PharmaCode barcode. It's
    measured in inches. Please see note at the end of barcode object. It shows inch
    calculating.

26- PharmaCodeThickBarWidth: Specifies the thick bar width of the PharmaCode
    barcode. It values are changing between 0.01 - 5.0 values. It's measured in inches.
    Please see note at the end of barcode object. It shows inch calculating.

27- PharmaCodeThinBarWidth: Specifies the thin bar width of the PharmaCode barcode.
    It values are changing between 0.01 - 5.0 values. It's measured in inches. Please see
    note at the end of barcode object. It shows inch calculating.

28- ShortBarHeight: Specifies the short bar height of the PLANET and POSTNET barcode.
    It values are changing between 0.01 - 5.0 values. It's measured in inches. Please see
    note at the end of barcode object. It shows inch calculating.

29- TallBarHeight: Specifies the tall bar height of the PLANET and POSTNET barcode. It
    values are changing between 0.01 - 5.0 values. It's measured in inches. Please see
    note at the end of barcode object. It shows inch calculating.

30- TelepenEncoding: Specifies encoding type of telepen barcode. It can be Ascii or
    Numeric.
       a. Ascii : Allows to use ascii(numeric+alphabetic) characters.
       b. Numeric : Allows to use only numeric characters.

31- UpceSystem : Specifiesthe number system to use for UPC-E symbology. It can be
    System0 and System1.
        a- System0
        b- System1

32- Text : Specifies text for barcode. Text doesn’t encode into barcode. İt is using only
    explanation.

33- Inverted : If you can print with white ribbon you must select White Ribbon option.
    The barcode's color inverted automatic. This property is useful when the user prints
    dark colored pack



                                        ~ 67 ~
                                 Savema Printer Programming Language (Rev.12)           03/08/2022

   34- CounterBegin : Specifies counter start value for barcode. This item is only used when
       Source item adjust as Counter.

   35- CounterEnd: Specifies counter end value for barcode. This item is only used when
       Source item adjust as Counter.

   36- CounterStep: Specifies counters step value. Deafult value is 1. For 1, counter increases
       one by one. This item is only used when Source item adjust as Counter.

   37- CounterPeriod: Specifies counter value increase after how many print. Deafult it is 1.
       This item is only used when Source item adjust as Counter.

   38- CounterDigit: Specifies digit of counter for counter variable barcode. This item is only
       used when Source item is Counter.

   39- FileName : This item stores file name(if possible with path) which keeps data. File
       must be csv file. İf file name will adjust in printer controller,İt can be default.csv.This
       item is only used when Source item adjust as database.

   40- ColumnNo : Specifies column of datas in csv file. İt is default 0.This item is only used
       when Source item adjust as database.

Note : Barcode component inch standart is 96 dpi but we are using 300 dpi(Print Head’s
resolution). So, you must be use this formula for calculating original size for savema printer :

Orginal Size = Value*(300/96 dpi) and 1 inch=25,4 mm

       For example : For 0.5 value equals 0.5 / (300/96 dpi)= 0.16 inch=4 mm

    Example       ~SPLTDS{<Template>
                        <General>
                   <MachineType>53*70 I</MachineType>
                   <Name>temp1_53.rox</Name>
                   <Width>640</Width>
                   <Height>912</Height>
                   <ZIndex>-1000</ZIndex>
                   <SaveImages>False</SaveImages>
                   <DataSourcesInfo/>
                   </General>
                   <DataSources/>
                   <Object>
                   <ObjectType>Barcode</ObjectType>

                                            ~ 68 ~
           Savema Printer Programming Language (Rev.12)   03/08/2022

 <NameID>Barcode08</NameID>
 <Name>barcode1</Name>
 <X>162</X>
 <Y>141</Y>
 <W>228</W>
 <H>93</H>
 <ZIndex>0</ZIndex>
 <Rotate>0</Rotate>
 <Hidden>False</Hidden>
 <Content>
 <Data>869123456789</Data>
 <Mirror>False</Mirror>
 <PromptMessage></PromptMessage>
 <Source>Internal</Source>
 <MergeFieldFormula></MergeFieldFormula>
 <MergeFieldItems></MergeFieldItems>
 <BarcodeType>EAN-13</BarcodeType>
 <BarcodeValue>869123456789</BarcodeValue>
 <AddCheckSum>True</AddCheckSum>
 <BarHeight>0.7</BarHeight>
 <BarRatio>2</BarRatio>
 <BarWidth>0.02</BarWidth>
 <BearerBarStyle>None</BearerBarStyle>
 <BearerBarWidth>0.02</BearerBarWidth>
 <BorderWidth>0</BorderWidth>
 <CodabarStartChar>A</CodabarStartChar>
 <CodabarStopChar>A</CodabarStopChar>
 <Code128Charset>Auto</Code128Charset>
 <CodeAlignment>BelowCenter</CodeAlignment>
 <DisplayChecksum>False</DisplayChecksum>
 <DisplayCode>True</DisplayCode>

<DisplayLightMarginIndicator>False</DisplayLightMarginIndicat
or>
 <DisplayStartStopChar>False</DisplayStartStopChar>
 <EanUpcSupplementType>None</EanUpcSupplementType>
 <EanUpcSupplementCode>0</EanUpcSupplementCode>

<EanUpcSupplementSeparation>0.2</EanUpcSupplementSepara
tion>

                     ~ 69 ~
                              Savema Printer Programming Language (Rev.12)        03/08/2022

                  <EanUpcSupplementMargin>0.2</EanUpcSupplementMargin>
                  <DrawGuardBar>True</DrawGuardBar>
                  <PharmaCodeBarSpacing>0.1</PharmaCodeBarSpacing>

                    <PharmaCodeThickBarWidth>0.1</PharmaCodeThickBarWidth>
                  <PharmaCodeThinBarWidth>0.1</PharmaCodeThinBarWidth>
                  <ShortBarHeight>0.1</ShortBarHeight>
                  <TallBarHeight>0.1</TallBarHeight>
                  <QuietZoneWidth>0.2</QuietZoneWidth>
                  <TelepenEncoding>Ascii</TelepenEncoding>
                  <UpceSystem>System0</UpceSystem>
                  <Text></Text>
                  <Inverted>False</Inverted>
                  <SegmentCount>6</SegmentCount>
                  <CounterBegin>1</CounterBegin>
                  <CounterEnd>999</CounterEnd>
                  <CounterStep>1</CounterStep>
                  <CounterPeriod>1</CounterPeriod>
                  <CounterDigit>3</CounterDigit>
                  <FileName>default.csv</FileName>
                  <ColumnNo>0</ColumnNo>
                 </Content>
                 <Font>
                  <Name>Arial</Name>
                  <Size>15</Size>
                  <Style>Regular</Style>
                 </Font>
                 </Object>
                     <Dates/>
                    </Template>}^ --- This template has only one barcode

3.1.3.9       2D Barcode

2D Barcode object have various properties which is shown in below,

  1- Source: This item specify barcode value source. it can be Internal and External.
         a- Internal : This is default selection for barcode. 2DBarcode value is identified
             from PC when creating a template in this mode.
         b- External : Multitext object gets value from RS-232 or Ethernet interface.
         c- Counter :2D Barcode object value is increasing one by one. 2D Barcode
             values must be numeric in this type.

                                         ~ 70 ~
                           Savema Printer Programming Language (Rev.12)       03/08/2022

        d- Database : 2D Barcode object value is changing one by on according to data
           file(.csv file) content. Csv file must be stored in controller.



2- TwoDBarcodeType : It stores barcode type.
3- We support many barcodes. These are;
      1. Code16k
      2. DataMatrix
      3. GS1-Datamatrix
      4. QRCode
      5. Semacode
      6. AztecCode
      7. Pdf417
      8. CompactPdf417
      9. MacroPdf417
      10. MicroPDF417



4- TwoDBarcodeValue: It stores barcode value.2D Barcode value must be suitable for
   barcode type.

5- ErrorCorrection
       • AztecCodeErrorCorrection: Specifies Error Correction Percentage to apply for
          Aztec Code symbology. Default is 23.
       • Pdf417ErrorCorrectionLevel : Specifies the Error Correction Level to apply for
          PDF417 symbology.
            a) Level0
            b) Level1
            c) Level2 : This is default selection.
            d) Level3
            e) Level4
            f) Level5
            g) Level6
            h) Level7
            i) Level 8

       •   QRCodeErrorCorrectionLevel: Specifies the Error Correction Level to apply for
           QR Code symbology. There are 4 type of Error Correction.
            a) L : Approx. 7% of codewords can be restored. Error correction level L is
                appropriate for high symbol quality and/or the need for the smallest
                possible symbol.

                                      ~ 71 ~
                          Savema Printer Programming Language (Rev.12)      03/08/2022

            b) M : Approx. 15% of codewords can be restored. Level M is described as
               Standard level and offers a good compromise between small size and
               increased reliability. We are using this encoding type as a default.
            c) Q : Approx. 25% of codewords can be restored. Level Q is a High
               reliability level and suitable for more critical or poor print quality
               applications.
            d) H : Approx. 30% of codewords can be restored. Level H offers the
               maximum achievable reliability.




6- CodeFormat :
      • AztecCodeFormat:Specifies the Aztec Code Format to use on that symbology.
         This property have alot of format. Auto is using as a default. Formats are ;
         ➢ Auto
         ➢ C15X15Compact
         ➢ C19X19
         ➢ C19X19Compact
         ➢ C23X23
         ➢ C23X23Compact
         ➢ C27X27
         ➢ C27X27Compact
         ➢ C31X31
         ➢ C37X37
         ➢ C41X41
         ➢ C45X45
         ➢ C49X49
         ➢ C53X53
         ➢ C57X57
         ➢ C61X61
         ➢ C67X67
         ➢ C71X71
         ➢ C75X75
         ➢ C79X79
         ➢ C83X83
         ➢ C87X87
         ➢ C91X91
         ➢ C95X95
         ➢ C101X101
         ➢ C105X105
         ➢ C109X109


                                    ~ 72 ~
                    Savema Printer Programming Language (Rev.12)        03/08/2022

    ➢   C113X113
    ➢   C117X117
    ➢   C121X121
    ➢   C125X125
    ➢   C131X131
    ➢   C135X135
    ➢   C139X139
    ➢   C143X143
    ➢   C147X147
    ➢   C151X151


•   DataMatrixFormat : Specifies the DataMatrix Format to use on that
    symbology.
     ➢ Auto : This is default selection.
     ➢ C10X10
     ➢ C12X12
     ➢ C14X14
     ➢ C16CX16
     ➢ C18X18
     ➢ C20X20
     ➢ C22X22
     ➢ C24X24
     ➢ C26X26
     ➢ C32X32
     ➢ C36X36
     ➢ C40X40
     ➢ C44X44
     ➢ C48X48
     ➢ C52X52
     ➢ C64X64
     ➢ C72X72
     ➢ C80X80
     ➢ C88X88
     ➢ C96X96
     ➢ C104X104
     ➢ C120X120
     ➢ C132X132
     ➢ C144X144
     ➢ C8X18
     ➢ C8X32
     ➢ C12X26

                              ~ 73 ~
                           Savema Printer Programming Language (Rev.12)        03/08/2022

           ➢ C12X36
           ➢ C16X36
           ➢ C16X48


7- ModuleSize :
      • AztecCodeModuleSize: Specifies the module sizeof Aztec Code. It's measured
          in inches. It is changing between 0.01 and 0.3. Please see note at the end of 2D
          barcode object. It shows inch calculating.
      • DataMatrixModuleSize : Specifies DataMatrix module size. It’s measured in
          inches.It is changing between 0.01 and 0.3. Please see note at the end of 2D
          barcode object. It shows inch calculating.
      • QRCodeModuleSize : Specifiesthe module size. It's measured in inches. Please
          see note at the end of 2D barcode object. It shows inch calculating.
      • GS1-DatamatrixModuleSize : Specifies GS1-DataMatrix module size. It’s
          measured in inches.It is changing between 0.01 and 0.3. Please see note at the
          end of 2D barcode object. It shows inch calculating.
8- Version :
      • MicroPDF417Version: Specifies the MicroPDF417 version (a predefined
          combinations of numbers of columns and rows) to be generated.
           ➢ Auto : This is default selection.
           ➢ V1X11
           ➢ V1X17
           ➢ V1X20
           ➢ V1X24
           ➢ V1X28
           ➢ V2X8
           ➢ V2X11
           ➢ V2X14
           ➢ V2X17
           ➢ V2X20
           ➢ V2X23
           ➢ V2X26
           ➢ V3X6
           ➢ V3X8
           ➢ V3X10
           ➢ V3X12
           ➢ V3X15
           ➢ V3X20
           ➢ V3X26
           ➢ V3X32
               V3X38
           ➢

                                      ~ 74 ~
                    Savema Printer Programming Language (Rev.12)       03/08/2022

    ➢   V3X44
    ➢   V4X4
    ➢   V4X6
    ➢   V4X8
    ➢   V4X10
    ➢   V4X12
    ➢   V4X15
    ➢   V4X20
    ➢   V4X26
    ➢   V4X32
    ➢   V4X38
    ➢   V4X44

•   QRCodeVersion : Specifiesthe QR Code Version to use on that symbology.
    Version 1 (21 x 21 modules) to Version 40 (177 x 177 modules) increasing in
    steps of four modules per side.
     ➢ Auto :
     ➢ V01
     ➢ V02
     ➢ V03
     ➢ V04
     ➢ V05
     ➢ V06
     ➢ V07
     ➢ V08
     ➢ V09
     ➢ V10
     ➢ V11
     ➢ V12
     ➢ V13
     ➢ V14
     ➢ V15
     ➢ V16
     ➢ V17
     ➢ V18
     ➢ V19
     ➢ V20
     ➢ V21
     ➢ V22
     ➢ V23
     ➢ V24

                               ~ 75 ~
                            Savema Printer Programming Language (Rev.12)        03/08/2022

          ➢ V25
          ➢ V26
          ➢ V27
          ➢ V28
          ➢ V29
          ➢ V30
          ➢ V31
          ➢ V32
          ➢ V33
          ➢ V34
          ➢ V35
          ➢ V36
          ➢ V37
          ➢ V38
          ➢ V39
          ➢ V40
9- Encoding :
      • DataMatrixEncoding : Specifies the DataMatrix Encoding to use on that
         symbology.
          a- Auto : This is default selection.
          b- Ascii : Used to encode data that mainly contains ASCII characters (0-127).
          c- C40 : Used to encode data that mainly contains numeric and upper case
              characters.
          d- Text : Used to encode data that mainly contains numeric and lower case
              characters.
          e- Base256 : Used to encode 8 bit values

       •   QRCodeEncoding : Specifies the QR Code Encoding to use on that symbology.
           a- Auto : This is default selection.
           b- Numeric : Used to encode data that mainly contains numeric characters.
           c- AlphaNumeric : Used to encode data that mainly contains alphanumeric
              characters.
           d- Kanji : Used to encode data that mainly contains Kanji characters.
           e- Byte : Used to encode 8 bit values.

10- AztecCodeRune: Specifies the Aztec Code Rune value. It must be a value from 0 to
    255 and is available for Aztec Code Compact Format only.

11- BarHeight : Specifies the bar’s height of the barcode. It's measured in inches. It is
    changing between 0.1 and 3. Only using with Code16k. Please see note at the end of
    2D barcode object. It shows inch calculating.

                                       ~ 76 ~
                             Savema Printer Programming Language (Rev.12)          03/08/2022



12- BarRatio : Specifies the wide bar’s width compared to the narrow bar’s width. In
    barcode terminology this is N value. It is changing between 1 and 30. Please see note
    at the end of 2D barcode object. It shows inch calculating.

13- BarWidth : Specifies the narrow bar’s width of the barcode. In barcode terminology
    this is X value. It's measured in inches. Please see note at the end of 2D barcode
    object. It shows inch calculating.

14- BorderWidth : Specifies the barcode image border's width. Border property is
    drawing frame around barcode. Default it is 0. If it is 0, cannot see any frame around
    barcode.



15- Code16kMode : Specifies the mode to use for Code16k symbology.It can be Mode0,
    Mode1 and Mode2.
    a- Mode0 : This will use the Code 128 Char Set A which only supports ASCII values
       from 0 to 95. It is using as a default.
    b- Mode1 : This will use the Code 128 Char Set B which only supports ASCII values
       from 32 to 127
    c- Mode2 : This will use the Code 128 Char Set C which only supports pairs of digits



16- Pdf417AspectRatio : Specifiesthe ratio of the height to the overall width of the
    PDF417 symbol. Its value must be 0 (zero) up to 1 (one). Default it is 0.

17- Pdf417Columns : Specifiesthe number of columns to use for PDF417 symbology.

18- Pdf417CompactionType : Specifies the Compaction Type to apply for PDF417
    symbology.
    a- Auto : It switches between Text, Binary and Numeric modes in order to minimize
       the number of codewords to be encoded.
    b- Binary : It allows encoding all 256 possible 8-bit byte values. This includes all ASCII
       characters value from 0 to 127 inclusive and provides for international character
       set support. It is using as a Default.
    c- Text : It allows encoding all printable ASCII characters, i.e. values from 32 to 126
       inclusive in accordance with ISO/IEC 646, as well as selected control characters
       such as TAB (horizontal tab ASCII 9), LF (NL line feed, new line ASCII 10) and CR
       (carriage return ASCII 13).
    d- Numeric : It allows encoding numeric data strings.



                                        ~ 77 ~
                                Savema Printer Programming Language (Rev.12)            03/08/2022



  19- Pdf417Rows : Specifies the number of rows to use for PDF417 symbology.

  20- Inverted : If you can print with white ribbon you must select White Ribbon option.
      The barcode's color inverted automatic. This property is useful when the user prints
      dark colored pack

  21- CounterBegin : Specifies counter start value for 2D barcode. This item is only used
      when Source item adjust as Counter.

  22- CounterEnd: Specifies counter end value for 2D barcode. This item is only used when
      Source item adjust as Counter.

  23- CounterStep: Specifies counters step value. Deafult it is 1. For 1, counter increases
      one by one. This item is only used when Source item adjust as Counter.

  24- CounterPeriod: Specifies counter value increase after how many print. Deafult it is 1.
      This item is only used when Source item adjust as Counter.

  25- CounterDigit: Specifies digit of counter for counter variable 2D barcode. This item is
      only used when Source item is Counter.

  26- FileName : This item stores file name(if possible with path) which keeps data. File
      must be csv file. If file name will adjust in printer controller, it can be default.csv.This
      item is only used when Source item is database.
  27- ColumnNo : Specifies column of datas in csv file. It is default 0.This item is only used
      when Source item is database.

Note : 2D Barcode component inch standart is 96 dpi but we are using 300 dpi(Print Head’s
resolution). So, you must be use this formula for calculating original size for savema printer

Orginal Size = Value*(300/96 dpi) and 1 inch=25,4 mm

      For example : For 0.5 value equals 0.5 / (300/96 dpi)= 0.16 inch=4 mm

    Example ~SPLTDS{<Template>
                     <General>
                <MachineType>53*70 I</MachineType>
                <Name>temp1_53.rox</Name>
                <Width>640</Width>
                <Height>912</Height>
                <ZIndex>-1000</ZIndex>
                <SaveImages>False</SaveImages>
                                           ~ 78 ~
           Savema Printer Programming Language (Rev.12)   03/08/2022

<DataSourcesInfo/>
</General>
<DataSources/>
<Object>
<ObjectType>2DBarcode</ObjectType>
<NameID>Barcode09</NameID>
<Name>2dbarcode1</Name>
<X>250</X>
<Y>115</Y>
<W>48</W>
<H>48</H>
<ZIndex>0</ZIndex>
<Rotate>0</Rotate>
<Hidden>False</Hidden>
<Content>
 <Data>123456789</Data>
 <Mirror>False</Mirror>
 <PromptMessage></PromptMessage>
 <Source>Internal</Source>
 <MergeFieldFormula></MergeFieldFormula>
 <MergeFieldItems></MergeFieldItems>
 <TwoDBarcodeType>DataMatrix</TwoDBarcodeType>
 <TwoDBarcodeValue>123456789</TwoDBarcodeValue>
 <ErrorCorrection>M</ErrorCorrection>
 <CodeFormat>Auto</CodeFormat>
 <ModuleSize>0.04</ModuleSize>
 <Version>Auto</Version>
 <Encoding>Auto</Encoding>
 <AztecCodeRune>1</AztecCodeRune>
 <BarHeight>0.4</BarHeight>
 <BarRatio>3</BarRatio>
 <BarWidth>0.02</BarWidth>
 <BorderWidth>0</BorderWidth>
 <Code16kMode>Mode0</Code16kMode>
 <Pdf417AspectRatio>0</Pdf417AspectRatio>
 <Pdf417Columns>5</Pdf417Columns>
 <Pdf417CompactionType>Binary</Pdf417CompactionType>
 <Pdf417Rows>0</Pdf417Rows>
 <Inverted>False</Inverted>
 <CounterBegin>1</CounterBegin>

                     ~ 79 ~
                               Savema Printer Programming Language (Rev.12)         03/08/2022

                <CounterEnd>999</CounterEnd>
                <CounterStep>1</CounterStep>
                <CounterPeriod>1</CounterPeriod>
                <CounterDigit>3</CounterDigit>
                <FileName>default.csv</FileName>
                <ColumnNo>0</ColumnNo>
               </Content>
               <Font>
                <Name></Name>
                <Size>12</Size>
                <Style>Regular</Style>
               </Font>
               </Object>
                  <Dates/>
                 </Template>}^ --- This template has only one 2d barcode


3.1.3.10       Database

Database object is used for printing random data in each print. It gets data from data
file(.csv) for print. Database object have four different properties which is shown in below,



    Exampl    ~SPLTDS{<Template>
    e               <General>
               <MachineType>53*70 I</MachineType>
               <Name>temp1_53.rox</Name>
               <Width>640</Width>
               <Height>912</Height>
               <ZIndex>-1000</ZIndex>
               <SaveImages>False</SaveImages>
               <DataSourcesInfo/>
               </General>
               <DataSources/>
               <Object>
               <ObjectType>Text</ObjectType>
               <NameID>Text013</NameID>
               <Name>text1</Name>
               <X>196</X>
               <Y>40</Y>

                                          ~ 80 ~
                       Savema Printer Programming Language (Rev.12)   03/08/2022

            <W>33</W>
            <H>63</H>
            <ZIndex>0</ZIndex>
            <Rotate>0</Rotate>
            <Hidden>False</Hidden>
            <Content>
            <Data>1</Data>
            <Source>VariableData</Source>
            <PromptMessage></PromptMessage>
            <AllowedCharacters>Any</AllowedCharacters>
            <DataSourceName>Text013</DataSourceName>
            <DataSourceColumn>Text013</DataSourceColumn>
            <ColumnDatas>1</ColumnDatas>
            <CSVFileFullName>C:/Users/SakirC/Desktop/New Microsoft Excel
           Çalışma Sayfası.csv</CSVFileFullName>
            <CSVFileName>New Microsoft Excel Çalışma
           Sayfası.csv</CSVFileName>
            <CSVFileColumnSeparator>,</CSVFileColumnSeparator>

           <CSVFileColumnSeparatorIndex>1</CSVFileColumnSeparatorIndex
           >
             <CSVFileColumnNo>0</CSVFileColumnNo>
             <MagnificationRatio>100</MagnificationRatio>
             <Inverted>False</Inverted>
             <Mirror>False</Mirror>
            </Content>
            <Font>
             <Name>Tahoma</Name>
             <Size>36</Size>
             <OriginalSize>12</OriginalSize>
             <Style>Regular</Style>
            </Font>
           </Object>
               <Dates/>
               </Template>}^ --- This template has only one richtext


3.1.3.11   Table




                                 ~ 81 ~
                  Savema Printer Programming Language (Rev.12)   03/08/2022

Example   ~SPLTDS{<Template>
               <General>
          <MachineType>53*70 I</MachineType>
          <Name>temp1_53.rox</Name>
          <Width>640</Width>
          <Height>912</Height>
          <ZIndex>-1000</ZIndex>
          <SaveImages>False</SaveImages>
          <DataSourcesInfo/>
          </General>
          <DataSources/>
          <Object>
          <ObjectType>Table</ObjectType>
          <NameID>Table010</NameID>
          <Name>table1</Name>
          <X>153</X>
          <Y>90</Y>
          <W>183</W>
          <H>243</H>
          <ZIndex>0</ZIndex>
          <Rotate>0</Rotate>
          <Hidden>False</Hidden>
          <Content>
           <Source>Internal</Source>
           <MagnificationRatio>100</MagnificationRatio>

          <ImageData>iVBORw0KGgoAAAANSUhEUgAAALcAAADzCAIAA
          ABLxRCyAAAACXBIWXMAAA7EAAAOxAGVKw4bAAADZklEQVR
          4nO3dwW2rWgBFUfKVAugg6YRW0oFLuLg0V5IW0oHfgAhl8rT
          9Bv4Eaa0JNqMjtAXDO03wd2OM+/3+uv85ds2Drtfr/tvm5/m5
          +dvWyyksy7JtXpbl6C2POuPmveatjf/+rzo5MZXQVEJTCU0lNJX
          QVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQV
          EJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJT
          CU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJ72S7
          7mbe/3O1223/b/Dz75jHGuq6HbuHXu1wuTp3mIa/b5XRvwsn
          mZ9o3z/P8fWs7y/4U9qe8LMvRWx51xs1jjJ9t+OLQVEJTCU0lNJ
          XQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQ
          VEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJ
          TCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCe
          1lu5zuNOTJ5mfaN48x1nU9dAu/3uVyceo0D3ndLqd7E042P9O

                            ~ 82 ~
                            Savema Printer Programming Language (Rev.12)     03/08/2022

                  +eZ7n71vbWfansD/lZVmO3vKoM24eY/xswxeHphKaSmgqoam
                  EphKaSmgqoamEphKaSmgqoamEphKaSmgqoamEphKaSmgqo
                  amEphKaSmgqoamEphKaSmgqoamEphKaSmgqoamEphKaSmg
                  qoamEphKaSmgqoamEphKaSmgqoamEphKaSmgqoamEphKaS
                  mgqoamEphKaSmgv2+V0pyFPNj/TvnmMsa7roVv49S6Xi1Onec
                  jrdjndm3Cy+Zn2zfM8f9/azrI/hf0pL8ty9JZHnXHzGONnG744NJ
                  XQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQ
                  VEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJ
                  TCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU0lNJXQVEJTCU
                  0lNJXQVEJ7nabper0ePeNR+2nIt9ttXddDtzzqjJtPlATH206dPnoF
                  v9jb29vn5+f9fn/Zzyo/i6+vr2ma5nk+esg/OOPmaZo+Pj7e39+na
                  XrxPiH9Aa7Ag2zhe9XSAAAAAElFTkSuQmCC</ImageData>
                    <Inverted>False</Inverted>
                    <Mirror>False</Mirror>

                  <TableResult>0;0#ns~qwerty~0;1#ns~qwerty~0;2#ns~qwerty
                  ~1;0#ns~qwerty~1;1#ns~qwerty~1;2#ns~qwerty~2;0#ns~qwe
                  rty~2;1#ns~qwerty~2;2#ns~qwerty~3;0#ns~qwerty~3;1#ns~q
                  werty~3;2#ns</TableResult>
                    <ColumnCount>3</ColumnCount>
                    <RowCount>4</RowCount>
                    <ListColumnWidth>20,20,20</ListColumnWidth>
                    <ListRowHeight>20,20,20,20</ListRowHeight>
                    <LineWidth>1</LineWidth>
                   </Content>
                   <Font>
                    <Name>Arial</Name>
                    <Size>18</Size>
                    <Style>Regular</Style>
                   </Font>
                   </Object>
                      <Dates/>
                      </Template>}^ --- This template has only one richtext


3.1.3.12     Merge Fields



Merge field is merged as the required type in a text or barcode field. Merge field can
combined time, date and counter into a single field.

                                       ~ 83 ~
                            Savema Printer Programming Language (Rev.12)    03/08/2022

Merge field can be using for building barcodes or texts from different elements or
database or from variable length user entered fields.



   Example        ~SPLTDS{<Template>
                        <General>
                   <MachineType>53*70 I</MachineType>
                   <Name>temp1_53.rox</Name>
                   <Width>640</Width>
                   <Height>912</Height>
                   <ZIndex>-1000</ZIndex>
                   <SaveImages>False</SaveImages>
                   <DataSourcesInfo/>
                  </General>
                  <DataSources/>
                  <Object>
                   <ObjectType>Text</ObjectType>
                   <NameID>Text013</NameID>
                   <Name>text1</Name>
                   <X>196</X>
                   <Y>40</Y>
                   <W>33</W>
                   <H>63</H>
                   <ZIndex>0</ZIndex>
                   <Rotate>0</Rotate>
                   <Hidden>False</Hidden>
                   <Content>
                   <Data>1</Data>
                   <Source>VariableData</Source>
                   <PromptMessage></PromptMessage>
                   <AllowedCharacters>Any</AllowedCharacters>
                   <DataSourceName>Text013</DataSourceName>
                   <DataSourceColumn>Text013</DataSourceColumn>
                   <ColumnDatas>1</ColumnDatas>
                   <CSVFileFullName>C:/Users/SakirC/Desktop/New Microsoft
                  Excel Çalışma Sayfası.csv</CSVFileFullName>
                   <CSVFileName>New Microsoft Excel Çalışma
                  Sayfası.csv</CSVFileName>
                   <CSVFileColumnSeparator>,</CSVFileColumnSeparator>



                                      ~ 84 ~
        Savema Printer Programming Language (Rev.12)   03/08/2022

<CSVFileColumnSeparatorIndex>1</CSVFileColumnSeparatorI
ndex>
 <CSVFileColumnNo>0</CSVFileColumnNo>
 <MagnificationRatio>100</MagnificationRatio>
 <Inverted>False</Inverted>
 <Mirror>False</Mirror>
 </Content>
 <Font>
 <Name>Tahoma</Name>
 <Size>36</Size>
 <OriginalSize>12</OriginalSize>
 <Style>Regular</Style>
 </Font>
</Object>
<Object>
 <ObjectType>Text</ObjectType>
 <NameID>Text015</NameID>
 <Name>text2</Name>
 <X>85</X>
 <Y>127</Y>
 <W>126</W>
 <H>63</H>
 <ZIndex>0</ZIndex>
 <Rotate>0</Rotate>
 <Hidden>False</Hidden>
 <Content>
 <Data>1Text</Data>
 <Source>MergeField</Source>
 <PromptMessage></PromptMessage>
 <AllowedCharacters>Any</AllowedCharacters>

<MergeFieldFormula>{Text013}&amp;{Text014}</MergeField
Formula>
 <MergeFieldItems>Text013~Text014</MergeFieldItems>
 <MagnificationRatio>100</MagnificationRatio>
 <Inverted>False</Inverted>
 <Mirror>False</Mirror>
 </Content>
 <Font>
 <Name>Tahoma</Name>

                  ~ 85 ~
        Savema Printer Programming Language (Rev.12)   03/08/2022

 <Size>36</Size>
 <OriginalSize>12</OriginalSize>
 <Style>Regular</Style>
</Font>
</Object>
<Object>
<ObjectType>Text</ObjectType>
<NameID>Text014</NameID>
<Name>text3</Name>
<X>82</X>
<Y>36</Y>
<W>99</W>
<H>63</H>
<ZIndex>0</ZIndex>
<Rotate>0</Rotate>
<Hidden>False</Hidden>
<Content>
 <Data>Text</Data>
 <Source>Internal</Source>
 <PromptMessage></PromptMessage>
 <AllowedCharacters>Any</AllowedCharacters>
 <MagnificationRatio>100</MagnificationRatio>
 <Inverted>False</Inverted>
 <Mirror>False</Mirror>
</Content>
<Font>
 <Name>Tahoma</Name>
 <Size>36</Size>
 <OriginalSize>12</OriginalSize>
 <Style>Regular</Style>
</Font>
</Object>
   <Dates/>
   </Template>}^ --- This template has only one richtext




                  ~ 86 ~
                                Savema Printer Programming Language (Rev.12)             03/08/2022

3.1.4      Font

This propery is used for adjusting object view and size. A lot of objects are using this
property. These are;

   a- Date
   b- Time
   c- Text
   d- Counter
   e- Barcode
   f- 2D Barcode
   g- Table
   h- RichText(Font information come from Rtf Data)

Some objects do not use this property. These are;

   a- Logo
   b- Shape

Font property have three different items. These are ;

   1- Name : Specifies name of font. The supported fonts are shown below: it can be add.
            a- Arial
            b- Courier New
            c- Gulim (for Korean language characters)
            d- Impact
            e- Simsun (For Chinese characters)
            f- SimHei (For Chinese characters)
            g- Tahoma
            h- Times New Roman
            i- Trebuchet MS
            j- Verdana
            k- Arabicfont (For Arabic characters)
            l- AMS_Arunalu (For Sinhalese characters)
            m- Sinhala – Kumudu (For Sinhalese characters)
            n- Radhika-PC (For Sinhalese characters)
            o- Sandaya (For Sinhalese characters)
            p- Sinhala InetFont (For Sinhalese characters)
            q- BNazanin (for Persian characters)

   2- Size : Specifies font size of object. Font size unit is point. Default 20pt.

   3- Style : Specifies style of text. It can be Regular, Bold, Italic or Bold,Italic.


                                            ~ 87 ~
                                 Savema Printer Programming Language (Rev.12)       03/08/2022

         a- Regular : This is default font style and it is normal text.
         b- Bold : Text is shown as a bold.
         c- Italic : Text is shown as an italic.
         d- Bold,Italic : Text is shown bold and italic at the same time.




3.2      Load Template File from Printer
         SPLLTF:Allows to load selected template file which is stored in printer. Template
         must be stored in printer otherwise printer doesn’t load this template.
         Printer sends OK message when loading template operation is successed or sends
         FAIL message when setting loading template operation is failed.


      Using         ~SPLLTF{Template File Name}^

                Parameters;
                   Template File Name : Specifies template file name which will be loaded.
                   Note: Savema template’s extension name is rox. Second numerical
                   extension(before .rox) is specified according to printer type. Can be _32
                   (for 32mm printers), _53 (for 53mm printers) and _107 .

                    Return Value(On Successed) :
                    ~ SPGRES{SPLLTF:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{SPLLTF:FAIL}^

      Example       ~SPLLTF{temp1_53.rox}^ -- temp1_53.rox file loads in printer.(if printer
                    has this template)



3.3      Get Active Template
         SPLGAT : Returns active working template name from printer. This command doesn’t
         have parameter.


      Using         ~SPLGAT^

      Example       ~SPLGAT^

                    Return Value(On Successed) :
                      ~ SPGRES{SPLGAT:temp1_53.rox}^ -- Printer sends active template name
                                                                   which name is temp1_53.rox
                    Note: Savema template’s extension name is rox. Second numerical
                    extension(before .rox) is specified according to printer type. Can be _32

                                            ~ 88 ~
                                Savema Printer Programming Language (Rev.12)        03/08/2022

                    (for 32mm printers), _53 (for 53mm printers) and _107 (107 mm printers).



3.4      Get Stored Templates
         SPLGST : Returns all stored template file names from printer.This command doesn’t
         have parameter.

      Using         ~SPLGST^

      Example       ~SPLGST^

                    Return Value(On Successed) :
                    ~ SPGRES{SPLGST:temp1_53.rox<abc_53.rox<temp2_53.rox}^ -- Printer
                    sends all template names from 53mm printers in SPGRES command
                    parameter.

                    ~ SPGRES{ SPLGST:temp1_32.rox<abc_32.rox<temp2_32.rox}^ -- Printer
                    sends all template names from 32mm printers in SPGRES command
                    parameter.

                    Note: Savema template’s extension name is rox. Second numerical
                    extension(before .rox) is specified according to printer type. Can be _32
                    (for 32mm printers), _53 aand _107 (107 mm printers).


3.5      Create Data File
         SPLCDF: Allows to create data file(.csv file) in printer . Needs two parameters.
         Printer sends OK message when creating data(.csv) file operation is successed or
         sends FAIL message when creating data(.csv) operation is failed.


      Using         ~SPLCDF{Data File Name~gt~File Content}^

                Parameters;
                   Data File Name : Specifies data file name which will be stored in printer.
                   File Content : This parameter must be arranged according to csv file rules.
                   Datas must be ordered per row and if use more than one column per row,
                   columns must be seperated with ~sc~ text.

                    Return Value(On Successed) :
                    ~ SPGRES{SPLCDF:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{SPLCDF:FAIL}^

      Example       sample.csv is created with 3 rows and 1 column in below;
                    ~SPLCDF{sample.csv~gt~abc1
                    bce1

                                           ~ 89 ~
                               Savema Printer Programming Language (Rev.12)        03/08/2022

                   cde1}^

                   sample.csv is created with 3 rows and 3 columns in below;
                   ~SPLCDF{sample.csv~gt~abc1~sc~abc2~sc~abc3
                   bce1~sc~bce2~sc~bce3
                   cde1~sc~cde2~sc~cde3}^


3.6      Get Stored Data Files
         SPLGSD: Returns all stored data file names from printer.This command doesn’t have
         parameter.

      Using        ~SPLGSD^

      Example      ~SPLGSD^

                   Return Value(On Successed) :
                   ~ SPGRES{SPLGSD:abc.csv<datafile1.csv}^ -- Printer sends all data file
                   names from printer in SPGRES command parameter.



3.7      Delete Template File
         SPLDTF : This command deletes selected template file from printer. This command
         uses template file name as a parameter.
         Printer sends OK message when deleting template file operation is successed or
         sends FAIL message when deleting templatefileoperation is failed.


      Using        ~SPLDTF{Template File Name}^

                Parameters;
                   Template File Name : Specifiestemplate file name which will be deleted.

                   Note: Savema template’s extension name is rox. Second numerical
                   extension(before .rox) is specified according to printer type. Can be _32
                   (for 32mm printers), _53 (for 53mm printers) and _107 (107 mm printers).

                   Return Value(On Successed) :
                   ~ SPGRES{SPLDTF:OK}^
                   Return Value(On Failed) :
                   ~ SPGRES{SPLDTF:FAIL}^

      Example      ~SPLDTF{temp1_53.rox}^ -- temp1_53.rox file is deleted from printer.



3.8      Delete All Templates

                                          ~ 90 ~
                                 Savema Printer Programming Language (Rev.12)         03/08/2022

         SPLDTA :This command deletes all stored template file from printer.User must be
         carefull before use this command. Because printer deletes all template file after get
         this command. This command doesn’t have parameter.
         Printer sends OK message when deleting all template files operation is successed or
         sends FAIL message when deleting template filesoperation is failed.


      Using         ~SPLDTA^

                    Return Value(On Successed) :
                    ~ SPGRES{SPLDTA:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{SPLDTA:FAIL}^

      Example       ~SPLDTA^ -- if printer has template file(s), all of them are deleted.



3.9      Delete Data File
         SPLDDF : This command deletes selected data file from printer. This command uses
         data file name as a parameter.
         Printer sends OK message when deleting data file operation is successed or sends
         FAIL message when deleting datafileoperation is failed.


      Using         ~SPLDDF{Data File Name}^

                Parameters;
                   Data File Name : Specifies data file name which will be deleted.

                    Return Value(On Successed) :
                    ~ SPGRES{SPLDDF:OK}^
                    Return Value(On Failed) :
                    ~ SPGRES{SPLDDF:FAIL}^

      Example       ~SPLDDF{datafile1.csv}^ -- datafile1.csv file is deleted from printer.



3.10 Delete All Data Files
         SPLDDA : This command deletes all stored data file from printer.User must be
         carefull before use this command. Because printer deletes all data file after get this
         command. This command doesn’t have parameter.
         Printer sends OK message when deleting all data files operation is successed or
         sends FAIL message when deleting all datafilesoperation is failed.



                                            ~ 91 ~
                              Savema Printer Programming Language (Rev.12)        03/08/2022

   Using         ~SPLDDA^

                 Return Value(On Successed) :
                 ~ SPGRES{SPLDDA:OK}^
                 Return Value(On Failed) :
                 ~ SPGRES{SPLDDA:FAIL}^

3.11 Clear Data Buffer
      SPLCDB : This command clears buffer which stored database datas as temporarily.
      When load template which have CSV database field, CSV datas and index of data(for
      start print) are loaded to data buffer. When delete CSV file, buffer should be cleared.
      This command doesn’t have parameter.
      Printer sends OK message when send this command to printer.

   Using         ~SPLCDB^

                 Return Value:
                 ~ SPGRES{SPLCDB:OK}^



3.12 Load Font File
      SPLLFF: Allows to load font file(.ttf file) into printer . Needs two parameters.
      Printer sends OK message when loading font file operation is successed or sends FAIL
      message when loading font file operation is failed.


   Using          ~SPLLFF{Font File Name>File Content}^

              Parameters;
                 Font File Name : Specifies font file name which will be sent to printer.
                 File Content : This parameters data must be read in binary format (as a
                 byte array) after that converted to base64 format.

                  Return Value(On Successed) :
                  ~ SPGRES{SPLLFF:OK}^
                  Return Value(On Failed) :
                  ~ SPGRES{SPLLFF:FAIL}^

   Example    ~SPLLFF{CENTURY.TTF>AAEAAAATAQAABAAwRFNJR/iDHXwAAmsAAAAagEx
              UU0i7RI/wAAAMiAAAAqJPUy8ydN5tGgAAAbgAAABWVkRNWAgm1vUAAA8s
              AAAXbmNtYXDa6Gk6AABlpAAABlJjdnQgrT+zvwAAdrAAAAUcZnBnbe485joAA
              Gv4AAAEgWdhc3AAGQAJAAJq8AAAABBnbHlmSH/pt
              *******************************
              Multiple Lines deleted
              *******************************

                                         ~ 92 ~
                             Savema Printer Programming Language (Rev.12)        03/08/2022

              pABD8btUIlP05gL/LxaNIb8AEOfPwhuC4MnD4alsNWb04K1ZrSwmOK9TC6Z2+
              1+Wm82BPIl5EipYMq/BTYIoL44U/tlak8PNztIEpeK/+IRNJLhuG2Lh3Me4RdPK
              Od8ZR7VujwwWfif/tKMvTkN3qa5jAsBMKtg3ZLOkBGqGWBVc9tBMycSJtG3H
              VE5dXRHPcfIDwP3hPp1xuGzjFlE+m6wmtOzvs/0v67QzxE14IGXtbAGFtAoz9d
              WQFFReySQlbIfI+oz+QoEaB0rASm17wwncAA=}^



3.13 Get Font Files
      SPLGFF: Returns all loaded font file names from printer.This command doesn’t have
      parameter.

   Using        ~SPLGFF^

   Example      ~SPLGFF^

                Return Value(On Successed) :
                ~ SPGRES{SPLGFF:arial.ttf<tahoma.ttf<verdana.ttf}^ -- Printer sends all
                font file names from printer in SPGRES command parameter.



3.14 Delete Font File
      SPLDFF : This command deletes selected font file from printer. This command uses
      font file name as a parameter.
      Printer sends OK message when deleting font file operation is successed or sends
      FAIL message when deleting font file operation is failed.


   Using        ~SPLDFF{Font File Name}^

             Parameters;
                Font File Name : Specifies font file name which will be deleted.If printer
                doesn’t have specified font file, command returns FAIL message.
                Note: Printer must be restart after delete any fonts.

                Return Value(On Successed) :
                ~ SPGRES{ SPLDFF:OK}^
                Return Value(On Failed) :
                ~ SPGRES{ SPLDFF:FAIL}^

   Example      ~SPLDFF{arial.ttf}^ -- arial.ttf file is deleted from printer.




                                         ~ 93 ~
                             Savema Printer Programming Language (Rev.12)        03/08/2022




3.15 Get Field Names
      SPLGFN : This command returns field names of selected template file which is stored
      in printer. This command uses template file name as a parameter.
      Printer sends field names with template name when get this command. .If printer
      doesn’t have specified template file, command returns FAIL message.




   Using        ~SPLGFN{Template File Name}^

             Parameters;
                Template File Name : Specifies template file name which is stored in
                printer.

                Return Value(On Successed) :
                ~ SPGRES{SPLGFN:template name<field name 1<field name 2<…}^
                Return Value(On Failed) :
                ~ SPGRES{SPLGFN:FAIL}^

   Example      ~SPLGFN{temp1_53.rox}^ -- Returns fields name of temp1_53.rox
                template.
                ~ SPGRES{SPLGFN: temp1_53.rox <Prod. Name<Prod. Dat<Exp.Date}^




3.16 Get Field Value
      SPLGFV : This command returns value of specified field in active template. This
      command uses field name as a parameter.
      Printer sends value of field with field name when get this command. .If printer
      doesn’t have specified field, command returns “<Field Name> not found” message.


   Using        ~SPLGFV{Field Name}^

             Parameters;
                Field Name : Specifies field name which is in active template.

                Return Value(On Successed) :
                ~ SPGRES{SPLGFV:Field name<Field value}^
                Return Value(On Failed) :


                                       ~ 94 ~
                             Savema Printer Programming Language (Rev.12)       03/08/2022

                ~ SPGRES{SPLGFV:<Field Name> not found}^

  Example       ~SPLGFV{BatchNo}^ -- Returns value of BatchNo field.
                ~ SPGRES{SPLGFV: BatchNo<AB00251 }^




3.17 Append Queue Datas
  SPLAQD: Allows to add dynamic data to specified queue. Printer creates queue
  automatically for each CSV field in template.Printer loading first datas to queue from
  related CSV file.(If Csv file datas will not use, queue content must be cleared with
  SPLCQD firstly and then use SPLAQD for new datas) Needs two parameters.
  Printer sends OK message when insert data to queue successfully or sends FAIL message
  when not insert.
  Note1: Queue system must be enabled in Authorization settings to get data from queue
  Note2: Queue works based on FIFO(First In First Out). In each print, the data is removed
  from the queue after it is read. Then arrives item count to 0, unless load new data to
  queue. If queue is empty(no item) or queue items finish, printer will stop automatically.


  Using         ~SPLAQD{Field Name~gt~ Datas}^

            Parameters;
               Field Name : Specifies field name in template which will be matched with
               queue. Each queue is created according to field name. If field name
               doesn’t existing, printer sends “<Field Name> not found message”.
               Datas : This parameter must be arranged according to csv file rules. Datas
               must be ordered per row.

                Return Value(On Successed) :
                ~ SPGRES{SPLAQD:OK}^
                Return Value(On Failed) :
                ~ SPGRES{SPLAQD:FAIL}^

  Example       3 rows is added into TextCSV field in below;
                ~SPLAQD{TextCSV~gt~abc1
                bce1
                cde1}^



                                       ~ 95 ~
                           Savema Printer Programming Language (Rev.12)        03/08/2022

3.18 Append Multi Queue Datas
  SPLAMQ: Allows to add dynamic data to more than one queues. This command is almost
  similar with SPLAQD command but provides to work with more than one queue as an
  extra. Needs minimum two parameters.It can be 4,6,8..etc parameters.
  Printer sends OK message when insert data to queues successfully or sends FAIL
  message when not insert.


  Using         ~SPLAMQ{Field Name1~gt~ Datas1~gt~ Field Name2~gt~ Datas2~gt~…}^

            Parameters;
               Field Name : Specifies field name in template which will be matched with
               queue. Each queue is created according to field name. If field name
               doesn’t existing, printer sends “<Field Name> not found message”.
               Datas : This parameter must be arranged according to csv file rules. Datas
               must be ordered per row.

                Return Value(On Successed) :
                ~ SPGRES{SPLAMQ:OK}^
                Return Value(On Failed) :
                ~ SPGRES{SPLAMQ:FAIL}^

  Example 3 rows is added into PRDNAME and BATCH NO field in below;
              ~SPLAMQ{PRDNAME~gt~PR01
               PR02
               PR03~gt~BATCH NO~gt~A01B
               A02B
               A03B}^


3.19 Get Queue Capacity
     SPLGQC: Returns item count in specified queue. Needs one parameter.
     Printer sends item count of specified queue or sends FAIL message when field not
     created or any error while processing that command.
     Note: Queue system must be enabled in Authorization settings to get data from
     queue


  Using         ~SPLGQC{Field Name}^

            Parameters;
               Field Name : Specifies field name in template which will be matched with
               queue. If field name doesn’t existing, printer sends “<Field Name> not
               found message”.



                                      ~ 96 ~
                           Savema Printer Programming Language (Rev.12)      03/08/2022

               Return Value(On Successed) :
               ~ SPGRES{SPLGQC:Item Count}^
               Return Value(On Failed) :
               ~ SPGRES{SPLGQC:FAIL}^
               Return Value(when TextCSV named field not existing) :
               ~ SPGRES{SPLGQC:< TextCSV > not found}^

  Example      ~SPLGQC{TextCSV}^

               Printer Sends: (When queue have 60 items)
               ~ SPGRES{SPLGQC:60}^

3.20 Get Multi Queue Capacity
     SPLGMQ: Returns item count in specified queues. Needs minimum one parameter. It
     can be 2,3,4…etc parameters.
     Printer sends item count of specified queues or sends FAIL message when field not
     created or any error while processing that command. This command is almost similar
     with SPLGQC command but provides to return more than one queue capacitiy as an
     extra.

  Using        ~SPLGMQ{Field Name1~gt~Field Name2~gt~Field Name3~gt~…}^

            Parameters;
               Field Name : Specifies field name in template which will be matched with
               queue. If field name doesn’t existing, printer sends “<Field Name> not
               found message”.

               Return Value(On Successed) :
               ~ SPGRES{SPLGQC:Field Name1=Item Count< Field Name2=Item Count}^
               Return Value(On Failed) :
               ~ SPGRES{SPLGQC:FAIL}^
               Return Value(when PRDNAME named field not existing) :
               ~ SPGRES{SPLGQC:< PRDNAME > not found}^

  Example      ~SPLGQC{PRDNAME~gt~BATCH NO}^

               Printer Sends: (When specified queues have 60 items)
               ~ SPGRES{SPLGQC:PRDNAME=60<BATCH NO=60}^




3.21 Clear Queue Datas
     SPLCQD: Allows to delete all items in specified queue. Needs one parameter.
     Printer sends OK message when deleted all items in queue or sends FAIL message
     when field not created or any error while processing that command.
                                      ~ 97 ~
                           Savema Printer Programming Language (Rev.12)      03/08/2022

     Note: Queue system must be enabled in Authorization settings to get data from
     queue


  Using        ~SPLCQD{Field Name}^

            Parameters;
               Field Name : Specifies field name in template which will be matched with
               queue. If field name doesn’t existing, printer sends “<Field Name> not
               found message”.

               Return Value(On Successed) :
               ~ SPGRES{SPLCQD:OK}^
               Return Value(On Failed) :
               ~ SPGRES{SPLCQD:FAIL}^
               Return Value(when TextCSV named field not existing) :
               ~ SPGRES{SPLCQD:< TextCSV > not found}^


  Example      ~SPLCQD{TextCSV}^




3.22 Clear Multi Queue Datas
     SPLCMQ: Allows to delete all items in specified queues. Needs minimum one
     parameter. It can be 2,3,4…etc parameters.
     Printer sends OK message when deleted all items in queue or sends FAIL message
     when field not created or any error while processing that command. This command is
     almost similar with SPLCQD command but provides to return more than one queue
     capacitiy as an extra.


  Using        ~SPLCMQ{Field Name1~gt~Field Name2~gt~Field Name3~gt~…}^

            Parameters;
               Field Name : Specifies field name in template which will be matched with
               queue. If field name doesn’t existing, printer sends “<Field Name> not
               found message”.

               Return Value(On Successed) :
               ~ SPGRES{SPLCMQ:OK}^
               Return Value(On Failed) :
               ~ SPGRES{SPLCMQ:FAIL}^
               Return Value(when PRDNAME named field not existing) :
               ~ SPGRES{SPLCMQ:< PRDNAME > not found}^

                                      ~ 98 ~
                   Savema Printer Programming Language (Rev.12)   03/08/2022




Example   ~SPLCMQ{PRDNAME~gt~BATCH NO}^




                             ~ 99 ~
                           Savema Printer Programming Language (Rev.12)        03/08/2022


4. Modification Commands
        Modification commands allows to chnage Text, Barcode and 2D barcode in a
        template. This commands are generally used to change one or more objects
        value at the same time in template over Ethernet communication. This
        commands can use one by one or with together.
        Source option of changable object(Text, Barcode and 2D barcode) must be
        External for modification.
        For external objects, data value must be carefullly checked.
        In template rotation 0 or 180 degrees, topmost or bottommost external object’s
        height and width carefully controlled. If control’s width/height exceeds template
        width/height, some clipping may occur.
        In template rotation 90 or 270 degrees, leftmost or rightmost external object’s
        height and width carefully controlled. If control’s width/height exceeds template
        width/height, some clipping may occur.

        Note : In order to these commands operating as intended and working properly,
        commands must be sended either machine is stop position or alternatively
        machine is print position and package is stop position.

4.1     Changing Text Value Commands
        SPMCTV: This command changes selected Text object value in template. Related
        text object must be in template. Otherwise commands doesn’t change any text.
        Printer sends OK message when changing text value operation is successed or
        sends FAIL message when changing tex valueoperation is failed. If field name
        doesn’t existing, printer sends “<Field Name> not found message”.

Using          ~SPMCTV{Name of object~gt~Text Value}^

           Parameters;
              Name of object: Please enter name of text object which is defined in PC
              software.
              Text Value :New value of selected text.

               Return Value(On Successed) :
               ~ SPGRES{SPMCTV:OK}^
               Return Value(On Failed) :
               ~ SPGRES{SPMCTV:FAIL}^
               Return Value(when TField1 named field not existing) :
               ~ SPGRES{SPMCTV:<TField1> not found}^

Example        ~SPMCTV{brand_txt~gt~SAVEMA}^ -- Set brand_txt value to “SAVEMA”
               ~SPMCTV{type_txt~gt~PRINTER}^ -- Set type_txt value to “PRINTER”


                                      ~ 100 ~
                           Savema Printer Programming Language (Rev.12)     03/08/2022

4.2     Changing Barcode Value Commands
        SPMCBV :This command changes selected Barcode(1D) object value in template.
        Related barcode object must be in template. Otherwise commands doesn’t
        change any barcode.
        Printer sends OK message when changing barcode value operation is successed
        or sends FAIL message when changing barcode valueoperation is failed. If field
        name doesn’t existing, printer sends “<Field Name> not found message”.


Using          ~SPMCBV{Name of object~gt~Barcode Value}^

           Parameters;
              Name of object: Please enter name of barcode object which is defined in
              PC software.
              Barcode Value :New value of selected barcode. Barcode value must be
              compatible with barcode type. Forexample EAN-13 barcode type accept
              only numerical characters and this value must be compatible with EAN-13
              rules.
              Barcode value characters count must be compatible with barcode type.
              Forexample, EAN-13 barcode accepts 12 or 13 numerical characters.

               Return Value(On Successed) :
               ~ SPGRES{SPMCBV:OK}^
               Return Value(On Failed) :
               ~ SPGRES{SPMCBV:FAIL}^
               Return Value(when BarF1 named field not existing) :
               ~ SPGRES{SPMCBV:< BarF1> not found}^

Example        ~SPMCBV{bar1~gt~8691234567890}^ -- Set bar1 value to
               8691234567890




4.3     Changing 2D Barcode Value Commands
        SPMC2D : This command changes selected 2D Barcode object value in template.
        Related 2D barcode object must be in template. Otherwise commands doesn’t
        change any 2D barcode.
        Printer sends OK message when changing 2D barcode value operation is
        successed or sends FAIL message when changing 2D barcode valueoperation is
        failed. If field name doesn’t existing, printer sends “<Field Name> not found
        message”.

Using           ~SPMC2D{Name of object~gt~Barcode Value}^



                                     ~ 101 ~
                           Savema Printer Programming Language (Rev.12)        03/08/2022

            Parameters;
               Name of object: Please enter name of 2D barcode object which is
               defined in PC software.
               Barcode Value :New value of selected 2D barcode. Barcode value must
               be compatible with barcode type. Forexample Datamatrix barcode
               accepts only standart ASCII characters but cannot accepts extra character
               without tandart ascii character. Forexample Datamatrix barcode type
               doesn’t accepts Ç,ü,ş,ö characters or arabic letters or chinese letters.

                Standart ASCII characters;
                !"#$%&'()*+,-
                ./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\]^_abcdefghij
                klmnopqrstuvwxyz{|}~

                Return Value(On Successed) :
                ~ SPGRES{SPMC2D:OK}^
                Return Value(On Failed) :
                ~ SPGRES{SPMC2D:FAIL}^
                Return Value(when ProductQRCode named field not existing) :
                ~ SPGRES{SPMC2D:< ProductQRCode> not found}^

Example         ~SPMC2D{b2bar1~gt~savema12345}^ -- Set b2bar1 value to
                savema12345




4.4     Changing Counter Value Commands
        SPMCCV :This command changes selected Counter object value in template.
        Related counter object must be in template. Otherwise commands doesn’t
        change any counter.
        Printer sends OK message when changing counter value operation is successed
        or sends FAIL message when changing counter value operation is failed. If field
        name doesn’t existing, printer sends “<Field Name> not found message”.


Using          ~SPMCCV{Name of object~gt~Counter Value}^

           Parameters;
              Name of object: Please entere name of counter object which is defined in
              PC software.
              Counter Value :New value of selected counter. Counter value must be
              numeric.

               Return Value(On Successed) :
               ~ SPGRES{SPMCCV:OK}^

                                      ~ 102 ~
                           Savema Printer Programming Language (Rev.12)        03/08/2022

               Return Value(On Failed) :
               ~ SPGRES{SPMCCV:FAIL}^
               Return Value(when batchcounter named field not existing) :
               ~ SPGRES{SPMCCV:< batchcounter > not found}^


Example        ~SPMCCV{counter1~gt~000055}^ -- Set counter1 value to 000055



4.5     Changing Logo Command
        SPMCLV :This command changes selected Logo object image in template.
        Related logo object must be in template. Otherwise commands doesn’t change
        any counter.
        Printer sends OK message when changing counter value operation is successed
        or sends FAIL message when changing counter value operation is failed. If field
        name doesn’t existing, printer sends “<Field Name> not found message”.

Using          ~SPMCLV{Name of object~gt~Base64 Data}^

           Parameters;
              Name of object: Please enter name of logo object which is defined in PC
              software.
              Base64 Data :This is base64 string of logo image.Sended logo must be
              converted to base64 string and then use as parameter in this command.
              This parameter character count increases according to image size.

               Return Value(On Successed) :
               ~ SPGRES{SPMCLV:OK}^
               Return Value(On Failed) :
               ~ SPGRES{SPMCLV:FAIL}^
               Return Value(when productlogo named field not existing) :
               ~ SPGRES{SPMCLV:< productlogo > not found}^

Example        ~SPMCCV{productlogo ~gt~/9j/4….Q== }^ -- …. shows remaining base64
               data

4.6     Changing Selected Values Command
        SPMCSV: This command changes selected Text, barcode and 2d barcode object’s
        value in template. Related object must be in template. Otherwise commands
        doesn’t change any object. You can modificate one or more objects value.
        Printer sends OK message when changing text value operation is successed or
        sends FAIL message when changing text valueoperation is failed. If field name
        doesn’t existing, printer sends “<Field Name> not found message”. If parameter



                                      ~ 103 ~
                          Savema Printer Programming Language (Rev.12)      03/08/2022

        of that command have more than one unused field name, printer sends first
        unused field name in parameter.


Using          ~SPMCSV{Name of object~gt~Value~gt~Name of object~gt~Value}^

           Parameters;
              Name of object: Please entere name of text object which is defined in PC
              software.
              Text Value :New value of selected text.

               Return Value(On Successed) :
               ~ SPGRES{SPMCSV:OK}^
               Return Value(On Failed) :
               ~ SPGRES{SPMCSV:FAIL}^
               Return Value(when fieldProName named field not existing) :
               ~ SPGRES{SPMCSV:< fieldProName > not found}^


Example        ~SPMCTV{brand_txt~gt~SAVEMA~gt~barcodeno~gt~123456789125}^ --
               Set brand_txt value to “SAVEMA” and barcode value as 123456789125




                                     ~ 104 ~
                              Savema Printer Programming Language (Rev.12)          03/08/2022


5. Print Commands
5.1 Start Print
   SPPSAP : This command starts printing automatically. Printer must be ready before send
   this command. Otherwise printer cannot start automatic printing. This command doesn’t
   have parameter.
   Printer sends OK message when starting print operation is successed or sends FAIL
   message when starting printoperation is failed.


   Using          ~SPPSAP^

                  Return Value(On Successed) :
                  ~ SPGRES{SPPSAP:OK}^
                  Return Value(On Failed) :
                  ~ SPGRES{SPPSAP:FAIL}^

   Example        ~SPPSAP^ -- if printer isready, printer starts printing automatically.



5.2 Set/Get Print Count for Limited print
   SPPSLQ :This commad specifies print quantity for limited prints. SPPSAP command must
   be send end of this command for start printing. Otherwise printer doesn’t start to print.
   Printer will stop after print quantity arrive to 0. This value is automatically decreasing
   one by one. This command have one parameter.
   Printer sends OK message when setting print quantity operation is successed or sends
   FAIL message when setting print quantityoperation is failed.

   Using          ~SPPSLQ{Limited Print Quantity}^

              Parameters;
                 Limited Print Quantity:Provides controlled printing. Printer works until
                 print quantity is 0.

                  Return Value(On Successed) :
                  ~ SPGRES{SPPSLQ:OK}^
                  Return Value(On Failed) :
                  ~ SPGRES{SPPSLQ:FAIL}^

   Example        ~SPPSLQ{1000}^ -- Limited Print Quantity is specified as 1000. Printer
                  doesn’t start printing.

                  ~SPPSLQ{1000}|SPPSAP^-- Printer prints 1000 prints and stop.


                                          ~ 105 ~
                             Savema Printer Programming Language (Rev.12)           03/08/2022



   SPPGLQ : This command retuns actual print quantity value. This command doesn’t have
   parameter.

   Using         ~SPCGLQ^

   Example       ~SPCGLQ^

                 Return Value(On Successed) :
                 ~ SPGRES{SPCGLQ:500}^ -- Print quantity is 500.



5.3 Stop Print

   SPPSTP :This command stops printing . Printer must be working before send this
   command. Otherwise printer cannot stop printing. This command doesn’t have
   parameter.

   Printer sends OK message when stop print operation is successed or sends FAIL
   message when stop printoperation is failed.



   Using         ~SPPSTP^

                 Return Value(On Successed) :
                 ~ SPGRES{SPPSTP:OK}^
                 Return Value(On Failed) :
                 ~ SPGRES{SPPSTP:FAIL}^

   Example       ~SPPSTP^ -- if printer is working , this command stops printing.


5.4 One Test Print

   SPPOTP :Provides to print one time . This command doesn’t have parameter.

   Printer sends OK message when print is successed or sends FAIL message when print is
   failed.

   Using         ~SPPOTP^

                 Return Value(On Successed) :
                 ~ SPGRES{SPPOTP:OK}^
                 Return Value(On Failed) :
                 ~ SPGRES{SPPOTP:FAIL}^

   Example       ~SPPOTP^ -- Printer prints one time.

                                        ~ 106 ~
                             Savema Printer Programming Language (Rev.12)       03/08/2022

5.5 Status of Printer

   SPPSTA : This command returns status of printer. There is 4 different response. These
   are INIT, WAITING, RUNNING and ERROR.

   1- INIT : Printer sends when controller software is loading in startup.(Before loaded
      template automatically). When template loaded automatically, WAITING message is
      sent by printer.
   2- WAITING : Printer sends when printer in stop mode.(Stop button pressed)
   3- RUNNING : Printer sends when printer in printing mode.(Print button pressed)
   4- ERROR: Printer sends when any error happens in printer. Printer sends error type in
      response message.

   This command doesn’t have parameter.

   Note: Printer sends FAIL message for all commands(except SPPSTA) when operator
   doesn’t in main window. SPPSTA command sends BLOCKED message with printer status.

   Using         ~SPPSTA^

                 Return Value(in startup- before loaded template) :
                 ~ SPGRES{SPPSTA:INIT<}^
                 Return Value(in stop mode) :
                 ~ SPGRES{SPPSTA:WAITING<}^
                 Return Value(in running mode) :
                 ~ SPGRES{SPPSTA:RUNNING<}^
                 Return Value(when error happens) :
                 ~ SPGRES{SPPSTA:ERROR<Error Content}^

           When operator doesn’t in main window;
             Return Value(in startup- before loaded template) :
             ~ SPGRES{SPPSTA:INIT<BLOCKED}^
             Return Value(in stop mode) :
             ~ SPGRES{SPPSTA:WAITING<BLOCKED}^
             Return Value(in running mode) :
             ~ SPGRES{SPPSTA:RUNNING<BLOCKED}^
             Return Value(when error happens) :
             ~ SPGRES{SPPSTA:ERROR<BLOCKED Error Content}^
   Example   ~SPPSTA^
             Return in error mode;
             ~ SPGRES{SPPSTA:ERROR< Ribbon not found.Please insert ribbon}^

                 When operator doesn’t in main window;
                 ~ SPGRES{SPPSTA:ERROR<BLOCKED Ribbon not found.Please insert
                 ribbon}^



                                        ~ 107 ~
                             Savema Printer Programming Language (Rev.12)      03/08/2022


6. General Commands
6.1 Send User Message to Printer
   SPGSUM : This command provides to show coming message to the printer display.
   Message can be received from PC, PLC or another device which sends this command.
   This command has one parameter.


   Using         ~SPGSUM{User Message}^

             Parameters;
                User Message :Sent from connected device with printer and showed on
                printer display. This command is used for warning purposes. So, it doesn’t
                affect printer.

   Example       ~SPGSUM{Package finished. Please stop printer}^ -- Printer has received
                 “Package finished. Please stop printer” message from pack machine.



6.2 General Response From Printer
   SPGRES :Returns all response from printer when request command is processed. So, this
   command cannot be used directly, only printer gives sends this command to connected
   device.
   This command have one parameter and this parameter content changes according to
   request command.

   Using          ~SPGRES{Response}^

              Parameters;
                 Response : This parameters content changes according to request
                 command.

   Example    ~SPGRES{SPGDTP:950225}^ -- Returns total print count

              ~SPGRES{SPCGPA:25<27<300<200<31<77<0<24<25<0<1265<0<5<0<23<0<4
              <0<0<400}^ --Retuns all sistem parameter..




                                       ~ 108 ~
                            Savema Printer Programming Language (Rev.12)       03/08/2022

6.3 Get Total Print Count
   SPGGTP : Returns total print count of printer.This command doesn’t have parameter.


   Using         ~SPGGTP^

   Example       ~SPGGTP^

                 Return Value(On Successed) :
                 ~ SPGRES{SPGGTP:458200}^ -- Printer printed 458200 prints since it
                 started working first.

6.4 Get Current Print Count
   SPGGCP : Returns current print count of printer.This counter resets when load any
   template. This count is shown in main window. This command doesn’t have parameter.


   Using         ~SPGGCP^

   Example       ~SPGGCP^

                 Return Value(On Successed) :
                 ~ SPGRES{SPGGTP:1250}^ -- Printer printed 1250 prints since load
                 template.

6.5 Get Firmware Version
   SPGGFV : Returns firmware version of printer. This command doesn’t have parameter.

   Using         ~SPGGFV^

                 Note:
   Example       ~SPGGFV^

                 Return Value(On Successed) :
                 ~ SPGRES{SPGGFV:6.3.001.600.R}^ -- Printer firmware version is
                 6.3.001.600.R




                                       ~ 109 ~
                                Savema Printer Programming Language (Rev.12)        03/08/2022

6.6 Get Remaining Ribbon(for printers models with Cassette)
   SPGGRR : Returns remaining ribbon percentage. This command is used with printer
   models with cassette.This command doesn’t have parameter.

   Using          ~SPGGRR^

                  Note:
   Example        ~SPGGRR^

                  Return Value(On Successed) :
                  ~ SPGRES{SPGGRR:80}^ -- Remaining ribbon amount is 80%.


6.7 Get Serial Number of Printer
   SPGGSN : Returns serial number of printer.This command doesn’t have parameter.

   Using          ~SPGGSN^

   Example        ~SPGGSN^

                  Return Value(On Successed) :
                  ~ SPGRES{SPGGSN:17013012}^ -- Printer serial number is 17013012.

6.8 Set Lock Interface
   SPGSLI : Allows to lock/unlock print,stop and edit buttons in printer interface. In this
   way, operator access is restricted. This command has one parameter.

   Using         ~SPGSLI{Lock/Unlock}^
              Parameters;
                 Lock/Unlock: 0 or 1 values are used as paramater.
                              0: Unlock print,stop and edit buttons
                              1: Lock print,stop and edit buttons.

                  Return Value(On Successed) :
                  ~ SPGRES{SPGSLI:OK}^.
                  Return Value(On Failed) :
                  ~ SPGRES{SPGSLI:FAIL}^.

   Example        ~SPGSLI{1}^




                                          ~ 110 ~
                             Savema Printer Programming Language (Rev.12)     03/08/2022

6.9 Get Lock Interface
   SPGGLI : Returns locking system is enabled or disabled.This command doesn’t have
   parameter.

   Using         ~SPGGLI^

                 Return Value(Print, stop and Edit buttons Locked) :
                 ~ SPGRES{SPGGLI:1}^
                 Return Value(Print, stop and Edit buttons Unlocked) :
                 ~ SPGRES{SPGGLI:0}^
   Example       ~SPGGLI^




                                       ~ 111 ~
                             Savema Printer Programming Language (Rev.12)        03/08/2022


7. TraverseCommands
  Traverse Commands are using onlyin traverse printers(TR53 and TR107). Traverse
  printers allows to print on multi-packages with one print signal. Traverse printer have
  some parameters and they are specified in below scheme.




  A- Pack Size(mm)
  B- Print Count(mm)
  C- Print Position in one package(mm)
  D- Package Distance from beginnig of package
  E- Printing area
7.1    Set/Get Pack Size(A)
SPTSPS: Allows to set one package size in multi-package.This value is mesaured with
millimeter.

  Using         ~SPTSPS{Pack size}^

            Parameters;
               Pack size:Specifies one partial package size in millimeter.Value must be
               between 1-3000.

                Return Value(On Successed) :
                ~ SPGRES{ SPTSPS:OK}^
                Return Value(On Failed) :
                ~ SPGRES{ SPTSPS:FAIL}^

  Example       ~SPTSPS{60}^


      SPTGPS : Returnsone package width in multi-package.

  Using         ~SPTGPS^

  Example       ~SPTGPS^

                Return Value(On Successed) :
                ~ SPGRES{ SPTGPS:60}^




                                        ~ 112 ~
                             Savema Printer Programming Language (Rev.12)         03/08/2022

7.2    Set/Get Print Count(B)
SPTSPC: Allows to set print count in one print signal. Print count must be specified
according to package count in multi-package.

  Using          ~SPTSPC{Print Count}^

             Parameters;
                Print Count: Specifies print count in one print signal Value must be
                between 1-3000.

                 Return Value(On Successed) :
                 ~ SPGRES{ SPTSPC:OK}^
                 Return Value(On Failed) :
                 ~ SPGRES{ SPTSPC:FAIL}^

  Example        ~SPTSPC{5}^


      SPTGPC : Returnsprint count.

  Using          ~SPTGPC^

  Example        ~SPTGPC^

                 Return Value(On Successed) :
                 ~ SPGRES{ SPTGPC:5}^



7.3    Set/Get Print Position(C)
SPTSPP: Allows to set print position of template from beginning of one package .This value
is mesaured with millimeter.

  Using          ~SPTSPP{Print Position}^

             Parameters;
                Print Position: Specifies print position of template. Value must be
                between 1-3000.

                 Return Value(On Successed) :
                 ~ SPGRES{ SPTSPP:OK}^
                 Return Value(On Failed) :
                 ~ SPGRES{ SPTSPP:FAIL}^

  Example        ~SPTSPP{10}^


                                        ~ 113 ~
                             Savema Printer Programming Language (Rev.12)        03/08/2022



      SPTGPP: Returns print position of template in one package.

  Using          ~SPTGPP^

  Example        ~SPTGPP^

                 Return Value(On Successed) :
                 ~ SPGRES{ SPTGPP:10}^



7.4    Set/Get Package Distance(D)
SPTSPD: Allows to set package distance from beginnig of printer printing area. This value
is mesaured with millimeter.

  Using          ~SPTSPD{Pack Distance}^

             Parameters;
                Pack Distance: Specifies package distance from beginning of printing
                area. Value must be between 0-3000.

                 Return Value(On Successed) :
                 ~ SPGRES{ SPTSPD:OK}^
                 Return Value(On Failed) :
                 ~ SPGRES{ SPTSPD:FAIL}^

  Example        ~SPTSPD{50}^


      SPTGPD: Returns package distance from beginning of printing area.

  Using          ~SPTGPD^

  Example        ~SPTGPD^

                 Return Value(On Successed) :
                 ~ SPGRES{ SPTGPD:50}^




                                        ~ 114 ~
                              Savema Printer Programming Language (Rev.12)        03/08/2022

7.5    Set/Get Printing Area(E)
SPTSPA: Allows to set total printing area of traverse printer. This value must be specified
according to traverse printer characteristics(max. printing area). This value is mesaured
with millimeter.

  Using          ~SPTSPA{Printing Area}^

             Parameters;
                Printing Area: Specifies printing area of traverse printer. Value must be
                between 0-3000.

                 Return Value(On Successed) :
                 ~ SPGRES{ SPTSPA:OK}^
                 Return Value(On Failed) :
                 ~ SPGRES{ SPTSPA:FAIL}^

  Example        ~SPTSPA{400}^


      SPTGPA: Returns printing area of traverse printer.

  Using          ~SPTGPA^

  Example        ~SPTGPA^

                 Return Value(On Successed) :
                 ~ SPGRES{ SPTGPA:400}^



7.6    Set/Get All Traverse Parameters
SPTSTP: Allows to set all traverse parameters for traverse printer.

  Using          ~SPTSTP{Pack Size>Print Count>Print Position>Pack Distance>Printing
                 Area}^


             Parameters;
                Pack size: Specifies one partial package size in millimeter.Value must be
                between 1-3000.
                Print Count: Specifies print count in one print signal. Value must be
                between 1-3000.
                Print Position: Specifies print position of template. Value must be
                between 1-3000.
                Pack Distance: Specifies package distance from beginning of printing


                                         ~ 115 ~
                         Savema Printer Programming Language (Rev.12)         03/08/2022

             area. Value must be between 0-3000.
             Printing Area: Specifies printing area of traverse printer. Value must be
             between 0-3000.

             Return Value(On Successed) :
             ~ SPGRES{SPTSTP:OK}^
             Return Value(On Failed) :
             ~ SPGRES{SPTSTP:FAIL}^

Example      ~SPTSTP{60>5>10>50>400}^


   SPTGTP: Returnsall traverse parameters.

Using        ~SPTGTP^

Example      ~SPTGTP^

             Return Value(On Successed) :
             ~ SPGRES{ SPTGTP:60<5<10<50<400}^




                                    ~ 116 ~
                              Savema Printer Programming Language (Rev.12)            03/08/2022


8. System Parameters Explanation

Parameter                                                                     Min.- Max.
                            32x40/50 I & 53x40/50 I
No                                                                            Value
P1        Motor ramp starting value (0-150)                                   0-150
P2        TPH down time (0-150)                                               0-150

P3        Ribbon and termal mechanic rewind speed (100-500)                   100-500

          Ribbon space (0-1000)
P4        [Ribbon space decrease < 200]                                       0-1000
          [Ribbon space increase > 200]


          Before the suspension point of the diameter of the surface of the
          ribbon hanging (10-60)
P5        [This value is constant for our ribbon winding system on printer]   10-60
          [Effect : Writing speed calculate system]
          [Factory Setting]



          After the suspension point of the diameter of the surface of the
          ribbon hanging (0-100)
P6        [This value is constant for our ribbon winding system on printer]   0-100
          [Effect : Writing speed calculate system]
          [Factory Setting]

          Vertical printing wait. when machine set up vetical, horizontal
P7                                                                            0-3000
          motor moment of inertia waiting (0-3000)
          Pre-Heating system control temperature as celsius (24-40)
P8                                                                            24-40
          [This value determine minimum TPH temperature]
          Before the suspension point of the diameter of the surface of the
          pulley (10-60)
P9                                                                            10-60
           [Effect : Writing speed calculate system]
          [Factory Setting]
          After the suspension point of the diameter of the surface of the
          pulley (0-100)
P10                                                                           0-100
          [Effect : Writing speed calculate system]
          [Factory Setting]
P11       TPH resistance value. (600-2000)                                    600-2000




                                          ~ 117 ~
                           Savema Printer Programming Language (Rev.12)           03/08/2022


      Change with 20. Parametric . Difficult material print. (0-2)
      [0: Easy mod. Max. Speed must be up to 500 (20. Parametic and on
      setting menu-> print speed]
P12   [1: Pre-difficult mod. Max. print Speed must be up to 300 (20.      0-2
      Parametic and on setting menu-> print speed]
      [2: Difficult mod. Max.print Speed must be up to 200 (20.
      Parametic and on setting menu-> print speed]

P13   Contact signal waiting value. (0-1000)                              0-1000


      Ribbon break active or inactive (0-1)
P14   [0: Ribbon break passive]                                           0-1
      [1: Ribbon break active]


      Print head printing stop position. (0-400)
P15   [ If the last out of the template does not transfered, value is     0-400
      increased.]
      Pre-heating system active or inactive selection (0-1)
P16   [0: Pre-heating system active]                                      0-1
      [1: Pre-heating system inactive]
P17   Ribbon break limitation (0-1000)                                    0-1000

      Printer fuse error warning active or inactive setting (0-3)
      [0: Main board fuse warning ACTIVE, Motor driver fuse warning
      ACTIVE]
      [1: Main board fuse warning INACTIVE, Motor driver fuse warning
P18   ACTIVE]                                                             0-3
      [2: Main board fuse warning ACTIVE, Motor driver fuse warning
      INACTIVE]
      [3: Main board fuse warning INACTIVE, Motor driver fuse warning
      INACTIVE]

      Fault output mode (0-1)
P19   [0: Machine gives alarms ERROR times and inactive times]            0-1
      [1: Machine gives alarm ERROR times]
P20   Max Print speed (150-500)                                           150-500

           Table-2) 32x40/50I and 53x40/50 I Parameters Explanation




                                       ~ 118 ~
                               Savema Printer Programming Language (Rev.12)             03/08/2022

Parameter                                                                       Min.- Max.
No
                  32x70 I & 53x70/125 I &107x75/125 I                           Value
P1          Motor ramp starting value (0-150)                                   0-150
P2          TPH down time (0-150)                                               0-150

P3          Ribbon and termal mechanic rewind speed (100-500)                   100-500

            Ribbon space (0-1000)
P4          [Ribbon space decrease < 200]                                       0-1000
            [Ribbon space increase > 200]


            Before the suspension point of the diameter of the surface of the
            ribbon hanging (10-60)
P5          [This value is constant for our ribbon winding system on printer]   10-60
            [Effect : Writing speed calculate system]
            [Factory Setting]



            After the suspension point of the diameter of the surface of the
            ribbon hanging (0-100)
P6          [This value is constant for our ribbon winding system on printer]   0-100
            [Effect : Writing speed calculate system]
            [Factory Setting]

            Vertical printing wait. when machine set up vertical, horizontal
P7                                                                              0-3000
            motor moment of inertia waiting (0-3000)
            Pre-Heating system control temperature as celsius (24-40)
P8                                                                              24-40
            [This value determine minimum TPH temperature]
            Before the suspension point of the diameter of the surface of the
            pulley (10-60)
P9                                                                              10-60
            [Effect : Writing speed calculate system]
            [Factory Setting]
            After the suspension point of the diameter of the surface of the
            pulley (0-100)
P10                                                                             0-100
            [Effect : Writing speed calculate system]
            [Factory Setting]
P11         TPH resistance value. (600-2000)                                    600-2000

            Change with 20. Parametric . Difficult material print. (0-2)
            [0: Easy mod. Max. Speed must be up to 500 (20. Parametic and on
            setting menu-> print speed]
P12         [1: Pre-difficult mod. Max. print Speed must be up to 300 (20.      0-2
            Parametic and on setting menu-> print speed]
            [2: Difficult mod. Max.print Speed must be up to 200 (20.
            Parametic and on setting menu-> print speed]

P13         Contact signal waiting value. (0-1000)                              0-1000


                                            ~ 119 ~
                             Savema Printer Programming Language (Rev.12)           03/08/2022


         Ribbon break and Ribbon not found active or passive (0-3)
         [0: Ribbon break active,Ribbon not found active]
P14      [1: Ribbon break passive,Ribbon not found active]                  0-3
         [2: Ribbon break passive,Ribbon not found passive]
         [3: Ribbon break and Ribbon not found are in switch mode]

         Print head printing stop position. (0-400)
P15      [ If the last out of the template does not transfered, value is    0-400
         increased.]
         Pre-heating system active or inactive selection (0-1)
P16      [0: pre-heating system active]                                     0-1
         [1:pre-heating system inactive]
P17      Ribbon break limitation (0-1000)                                   0-1000

         Printer fuse error warning active or inactive setting (0-3)
         [0: Main board fuse warning ACTIVE, Motor driver fuse warning
         ACTIVE]
         [1: Main board fuse warning INACTIVE, Motor driver fuse warning
P18      ACTIVE]                                                            0-3
         [2: Main board fuse warning ACTIVE, Motor driver fuse warning
         INACTIVE]
         [3: Main board fuse warning INACTIVE, Motor driver fuse warning
         INACTIVE]

         Fault output mode (0-1)
P19      [0: Machine gives alarms ERROR times and inactive times]           0-1
         [1: Machine gives alarm ERROR times]
P20      Max Print speed (150-500)                                          150-500

      Table-3) 32x70I and 53x70/125I and 107x75/125 I Parameters Explanation




                                         ~ 120 ~
                                Savema Printer Programming Language (Rev.12)                03/08/2022

Parameter                                                                               Min.- Max.
   No                                       32C                                           Value

            Measuring the diameter of the encoder as milimeter (10-70)
P1           [Effect : Substrate speed detection system]                            10-70
            [Factory setting]

            Pulse number of the encoder (800-1500)
P2                                                                                  80-1500
            [Factory setting]
            Diameter of red roller (20-60)
            [This value is constant for our ribbon winding system on printer]
P3                                                                                  20-60
            [Effect : Writing speed calculate system]
            [Factory setting]
            Diameter of red roller decimal portion value (0-100)
            [This value is constant for our ribbon winding system on printer]
P4                                                                                  0-100
            [effect : writing speed calculate system]
            [Factory setting]
            Reserved (0)
P5                                                                                  0
            [Factory setting]
P6          TPH Hold(pressed) encoder pulse number (0-600)                          0-600

            Motor ramp distance as milimeter (0-70)
P7                                                                                  0-70
            [Factory setting]

            TPH mechanism down time as milisecond (0-50)
P8                                                                                  0-50
            [Factory setting]
            Ribbon space decrease (0-1000)
P9          [This value decrease the gap of two printout on ribbon. ]               0-1000
            [Effect: Each 10 motor pulse effect 1mm]
            Ribbon space increase (0-1000)
P10         [This value increase the gap of two printout on ribbon. ]               0-1000
            [effect:each 10 motor pulse effect 1mm]
P11         TPH resistance value (600-2000)                                         600-2000
            Ribbon Settings
            [0 : Wax-resin + Easy package, Speed to 600]
            [1 : Wax-resin + Difficult package or Resin + Easy Package, Speed to
P12                                                                                 0-2
            550]
            [2 : Wax-resin + Very difficult package or Resin + Difficult package,
            Speed to 400]




                                            ~ 121 ~
                            Savema Printer Programming Language (Rev.12)           03/08/2022


      Continous prints modes (0-5)
      [0 : Working speed 30-300 mm/sc, min. pack size=50mm+
      Template Size]
      [1 : Working speed 30-350 mm/sc, min. pack size=50mm+
      Template Size]
      [2: Working speed 30-400 mm/sc, min. pack size=55mm+
P13   Template Size]                                                       0-5
      [3: Working speed 30-450 mm/sc, min. pack size=60mm+
      Template Size]
      [4: Working speed 30-550 mm/sc, min. pack size=65mm+
      Template Size]
      [5: Working speed 30-600 mm/sc, min. pack size=70mm+
      Template Size]


      Conservation value of the minimum speed as mm/sc. (0-1000)
P14   [This value is cancel limit when band speed less than this value,    0-1000
      while printer printing]

      Pre-Heating system control temperature as celsius (24-40)
P15                                                                        24-40
      [This value determine minimum TPH temperature]
      Pre-heating system active or inactive selection (0-1)
P16   [0: Pre-heating system active]                                       0-1
      [1: Pre-heating system inactive]
      Reserved (0)
P17                                                                        0
      [Factory setting]

      Printer fuse error warning active or inactive setting (0-3)
      [0: Main board fuse warning ACTIVE, Motor driver fuse warning
      ACTIVE]
      [1: Main board fuse warning INACTIVE, Motor driver fuse warning
P18   ACTIVE]                                                              0-3
      [2: Main board fuse warning ACTIVE, Motor driver fuse warning
      INACTIVE]
      [3: Main board fuse warning INACTIVE, Motor driver fuse warning
      INACTIVE]

      Fault output mode (0-1)
P19   [0: Machine gives alarms ERROR times and inactive times]             0-1
      [1: Machine gives alarm ERROR times]
      Determine the direction of ENCODER (0-1)
P20   [0: anti-clock wise rotation]                                        0-1
      [1: clock wise rotation]

                          Table-4) 32C Parameters Explanation




                                      ~ 122 ~
                               Savema Printer Programming Language (Rev.12)               03/08/2022

Parameter                                                                           Min.- Max.
No                              32 CC & 53 C &107 C                                 Value
            Measuring the diameter of the encoder as milimeter (10-70)
P1          [Effect : Substrate speed detection system]                             10-70
            [Factory setting]

P2          Pulse number of the encoder (80-1500)                                   80-1500
            [Factory setting]
            Diameter of red roller (20-60)
P3          [This value is constant for our ribbon winding system on printer]       20-60
            [Effect : Writing speed calculate system]
            [Factory setting]
            Diameter of red roller decimal portion value (0-100)
P4          [This value is constant for our ribbon winding system on printer]       0-100
            [Effect : Writing speed calculate system]
            [Factory setting]

P5          Reserved (0)                                                            0
            [Factory setting]
P6          TPH Hold(pressed) encoder pulse number (0-600)                          0-600


            Ribbon break and Ribbon not found active or passive (0-3)
P7          [0: Ribbon break active,Ribbon not found active]                        0-3
            [1: Ribbon break passive,Ribbon not found active]
            [2: Ribbon break passive,Ribbon not found passive]
            [3: Ribbon break and Ribbon not found are in switch mode]

P8          TPH mechanism down time as milisecond (0-50)                            0-50
            [Factory setting]
            Ribbon space decrease (0-1000)
P9          [This value decrease the gap of two printout on ribbon.]                0-1000
            [Effect: Each 10 motor pulse effect 1mm]
            Ribbon space increase (0-1000)
P10         [This value increase the gap of two printout on ribbon.]                0-1000
            [Effect: Each 10 motor pulse effect 1mm]
P11         TPH resistance value. (600-2000)                                        600-2000
            Ribbon Settings
            [0 : Wax-resin + Easy package, Speed to 800(32CC,53C), 600(107C)]
            [1 : Wax-resin + Difficult package or Resin + Easy Package, Speed to
P12                                                                                 0-2
            550]
            [2 : Wax-resin + Very difficult package or Resin + Difficult package,
            Speed to 400]




                                           ~ 123 ~
                           Savema Printer Programming Language (Rev.12)              03/08/2022



        Continous prints modes (0-5)
        [0 : Working speed 30-400 mm/sc, min. pack size=50mm+ Template
        Size]
        [1 : Working speed 30-450 mm/sc, min. pack size=50mm+ Template
        Size]
P13     [2: Working speed 30-500 mm/sc, min. pack size=55mm+ Template          0-5
        Size]
        [3: Working speed 30-650 mm/sc, min. pack size=60mm+ Template
        Size]
        [4: Working speed 30-750 mm/sc, min. pack size=65mm+ Template
        Size]
        [5: Working speed 30-800 mm/sc, min. pack size=75mm+ Template
        Size]


P14     Conservation value of the minimum speed as mm/sc. (0-1000)              0-1000
        [This value is cancel limit when band speed less than this value, while
        printer printing]
P15     Pre-Heating system control temperature as celsius (24-40)              24-40
        [This value determine minimum TPH temperature]
        Pre-heating system active or inactive selection (0-1)
P16     [0: Pre-heating system active]                                         0-1
        [1: Pre-heating system inactive]

P17                                                                            0-1000
        Ribbon break limitation (0-1000)

        Printer fuse error warning active or inactive setting (0-3)
        [0: Main board fuse warning ACTIVE, Motor driver fuse warning
        ACTIVE]
        [1: Main board fuse warning INACTIVE, Motor driver fuse warning
P18                                                                            0-3
        ACTIVE]
        [2: Main board fuse warning ACTIVE, Motor driver fuse warning
        INACTIVE]
        [3: Main board fuse warning INACTIVE, Motor driver fuse warning
        INACTIVE]
        Fault output mode (0-1)
P19     [0: Machine gives alarms ERROR times and inactive times]               0-1
        [1: Machine gives alarm ERROR times]
        Determine the direction of ENCODER (0-1)
P20     [0: Anti-clock wise rotation]                                          0-1
        [1: Clock wise rotation]


      Table-5) 32C with Cassette and 53C and 107C I Parameters Explanation




                                       ~ 124 ~
                              Savema Printer Programming Language (Rev.12)           03/08/2022

Parameter                                                                    Min.- Max.
                             32TR & 53TR & 107TR
No                                                                           Value
P1        Motor ramp starting value (0-30)                                   0-30
P2       TPH down time (0-30)                                                0-30

P3       Ribbon and thermal mechanic rewind speed (150-700)                  150-700

         Ribbon space (50-1000)
P4       [Ribbon space decrease < 200]                                       50-1000
         [Ribbon space increase > 200]



         Before the suspension point of the diameter of the surface of the
         ribbon hanging (10-60)
P5                                                                           10-60
         [This value is constant for our ribbon winding system on printer]
         [Factory Setting]




         After the suspension point of the diameter of the surface of the
         ribbon hanging (0-99)
P6                                                                           0-99
         [This value is constant for our ribbon winding system on printer]
         [Factory Setting]


P7       Ribbon reduction mode value (0-600)                                 0-600

         Pre-Heating system control temperature as celsius (24-45)
P8                                                                           24-45
         [This value determine minimum TPH temperature]

P9       TPH Pressed minimum value for less than 300 mm/sec.(0-50)           0-50


P10      TPH Pressed minimum value for more than 300 mm/sc. (0-50)           0-50

P11      Printing point constant(0-30)                                       0-30

         Change with 20. Parametric . Difficult material print. (0-2)
         [0: Easy mod. Max. Speed must be up to 500 (20. Parametic and on
         setting menu-> print speed]
P12      [1: Pre-difficult mod. Max. print Speed must be up to 500 (20.      0-2
         Parametic and on setting menu-> print speed]
         [2: Difficult mod. Max.print Speed must be up to 450 (20.
         Parametic and on setting menu-> print speed]

P13      [Factory Setting] (0-30)                                            0-30




                                         ~ 125 ~
                           Savema Printer Programming Language (Rev.12)           03/08/2022



      Ribbon break active or inactive (0-1)
P14   [0: Ribbon break passive]                                           0-1
      [1: Ribbon break active]


      Print head printing stop position. (0-400)
P15   [ If the last out of the template does not transfered, value is     0-500
      increased.]
      Pre-heating system active or inactive selection (0-1)
P16   [0: Pre-heating system active]                                      0-1
      [1: Pre-heating system inactive]
P17   Print head pressed distance before printing (0-50)                  0-50

      Printer fuse error warning active or inactive setting (0-3)
      [0: Main board fuse warning ACTIVE, Motor driver fuse warning
      ACTIVE]
      [1: Main board fuse warning INACTIVE, Motor driver fuse warning
P18   ACTIVE]                                                             0-3
      [2: Main board fuse warning ACTIVE, Motor driver fuse warning
      INACTIVE]
      [3: Main board fuse warning INACTIVE, Motor driver fuse warning
      INACTIVE]

      Fault output mode (0-1)
P19   [0: Machine gives alarms ERROR times and inactive times]            0-1
      [1: Machine gives alarm ERROR times]
P20   Max Print speed (150-700)                                           150-700

           Table-6) 32x40/50I and 53x40/50 I Parameters Explanation




                                       ~ 126 ~
                           Savema Printer Programming Language (Rev.12)        03/08/2022


9. Warnings
   Savema printer have two types of communication. These are Ethernet(TCP/IP) and RS-
   232. RS-232 allow max. 115200 bps for communication but Ethernet is faster than RS-
   232. So, long commands with parameters, can transfer lately in RS-232.

   Some commands are need time in printer side.(Load template, Chnage system
   paramaters …etc). So, connected device must wait until finish processing of this
   command.

   Modification commands allows to change selected object while printer printing.So,
   large amount of objects can update lately. For prevent this issue, variable object size
   adjust according to print per minute. Each variable things needs time to update. So, all
   variable objects must be measured according top rint per minute
   Variable objects are;
    - Date(Change according to sytem date)
    - Time(Change accoding to system time)
    - Counter (Changes according to each print )
    - Shift Code(Changes according to shifting time)
    - Variable Text (Change according to external data)
    - Variable Barcode (Change according to each print or external data)
    - Variable 2D Barcode (Change according to each print or external data)

   Because of the character limitations of the label and command format, following
   characters must not be used. Use the equivalent form of the character instead.

   Character          Equivalent           Converted Form             Example Form
   “                  &quot;               abc&quot;def               abc”def
   ‘                  &apos;               abc&apos;def               abc’def
   <                  &lt;                 abc&lt;def                 abc<def
   >                  &gt;                 abc&gt;def                 abc>def
   &                  &amp;                abc&amp;def                abc&def

                            Table-7) Character limitations




                                      ~ 127 ~
                            Savema Printer Programming Language (Rev.12)     03/08/2022

9.1    Command Limitations
      Some Commands are limited to use according to below table.
      Command Explanation             Supported By (Printer Types)
                                      All Intermittent and All Traverse Printers
      SPCSPS     Set Print Speed      32x40I, 32x50I, 32x70I, 53x40I, 53x50I, 53x70I,
                                      107x75I, 107x125I, TR32, TR53, TR107
                                      All Intermittent and All Traverse Printers
      SPCGPS     Get Print Speed      32x40I, 32x50I, 32x70I, 53x40I, 53x50I, 53x70I,
                                      107x75I, 107x125I, TR32, TR53, TR107
                                      All Continuous Printers
                 Set Internal Contact
      SPCSIC                          32C, 32C with Cassette, 32X250C, 53C, 53x250C,
                 Mode
                                      107C
                                      All Continuous Printers
                 Get Internal Contact
      SPCGIC                          32C, 32C with Cassette, 32X250C, 53C, 53x250C,
                 Mode
                                      107C
                                      All Continuous Printers
                 Set Trigger Contact
      SPCSTC                          32C, 32C with Cassette, 32X250C, 53C, 53x250C,
                 Mode
                                      107C
                                      All Continuous Printers
                 Get Trigger Contact
      SPCGTC                          32C, 32C with Cassette, 32X250C, 53C, 53x250C,
                 Mode
                                      107C
                                      All Intermittent and All Traverse Printers
      SPPOTP     One Test Print       32x40I, 32x50I, 32x70I, 53x40I, 53x50I, 53x70I,
                                      107x75I, 107x125I, TR32, TR53, TR107
                                      All with casette modells
                 Get Remaining
      SPGGRR                          32x70I, 32C wth Casette, 53C, 53X250C, 53x70I,
                 Ribbon
                                      53x125I, 107x75I, 107x125I, 107C
                                      All Traverse Printers
      SPTSPS     Set Pack Size
                                      TR32, TR53, TR107
                                      All Traverse Printers
      SPTGPS     Get Pack Size
                                      TR32, TR53, TR107
                                      All Traverse Printers
      SPTSPC     Set Print Count
                                      TR32, TR53, TR107
                                      All Traverse Printers
      SPTGPC     Get Print Count
                                      TR32, TR53, TR107
                                      All Traverse Printers
      SPTSPP     Set Print Position
                                      TR32, TR53, TR107
                                      All Traverse Printers
      SPTGPP     Get Print position
                                      TR32, TR53, TR107
                 Set Pack Distance    All Traverse Printers
      SPTSPD
                 From Beginning       TR32, TR53, TR107
                 Get Pack Distance    All Traverse Printers
      SPTGPD
                 From Beginning       TR32, TR53, TR107
                                      All Traverse Printers
      SPTSPA     Set Printing Area
                                      TR32, TR53, TR107

                                      ~ 128 ~
                                       Savema Printer Programming Language (Rev.12)            03/08/2022

                                                    All Traverse Printers
         SPTGPA       Get Printing Area
                                                    TR32, TR53, TR107
                      Set All Traverse              All Traverse Printers
         SPTSTP
                      Parameters                    TR32, TR53, TR107
                      Get All Traverse              All Traverse Printers
         SPTGTP
                      Parameters                    TR32, TR53, TR107

                                       Table-8) Command Limitations



 9.2      Command Operating Conditions
                                             COMMAND LIST
Command      Explanation                                      Operating Condition

                                     CONFIGURATION COMMANDS
SPCSDT       Set System Date&Time and Time Offset             Stop Position
SPCGDT       Get System Date&Time and Time Offset             Both (Print and Stop position)
SPCSNC       Set Network Configuration                        Stop Position
SPCGNC       Get Network Configuration                        Both
SPCSSC       Set RS-232 Configuration                         Stop Position
SPCGSC       Get RS-232 Configuration                         Both
SPCSPS       Set Print Speed                                  Stop Position
SPCGPS       Get Print Speed                                  Both
SPCSPD       Set Print Delay value                            Both
SPCGPD       Get Print Delay value                            Both
SPCSDV       Set Darkness(Contrast) Value                     Both
SPCGDV       Get Darkness(Contrast) Value                     Both
SPCSPR       Set Print Rotation                               Stop Position
SPCGPR       Get Print Rotation                               Both
SPCSHP       Set Horizontal Position                          Both
SPCGHP       Get Horizontal Position                          Both
SPCSMO       Set Mirroring Option                             Stop Position
SPCGMO       Get Mirroring Option                             Both
SPCSRS       Set RibbonSave Mode                              Stop Position
SPCGRS       Get RibbonSave Mode                              Both
SPCSIC       Set Internal Contact Mode                        Stop Position
SPCGIC       Get Internal Contact Mode                        Both
SPCSTC       Set Trigger Contact Mode                         Stop Position
SPCGTC       Get Trigger Contact Mode                         Both
SPCSAS       Set All Settings                                 Both
SPCGAS       Get All Settings                                 Both
SPCSSP       Set System Parameter                             Stop Position
SPCGSP       Get System Parameter                             Both
SPCSPA       Set All System Parameters                        Stop Position
SPCGPA       Get All System Parameters                        Both
SPCSSL       Set System Language                              Both


                                                 ~ 129 ~
                                  Savema Printer Programming Language (Rev.12)                  03/08/2022

SPCGSL   Get System Language                            Both
SPCSAP   Set Administrator Password                     Both
SPCGAP   Get Administrator Password                     Both
SPCSFS   Return to Factory Settings                     Both
SPCSPM   Set Print Request Message                      Both
SPCGPM   Get Print Request Message                      Both

                                LABEL DESIGNING COMMANDS
SPLTDS   Create Template Datas and Template Structure   Stop Position
SPLLTF   Load Template from Printer                     Stop Position
SPLGAT   Get Active Template                            Both
SPLGST   Get Stored Templates                           Both
SPLCDF   Create Data File                               Both
SPLGSD   Get Stored Data Files                          Both
SPLDTF   Delete Template                                Both
SPLDTA   Delete All Template                            Both
SPLDDF   Delete Data File                               Both
SPLDDA   Delete All Data File                           Both
SPLCDB   Clear Data Buffer                              Both
SPLGFN   Get Field Names                                Both
SPLGFV   Get Field Value                                Both
SPLAQD   Append Queue Datas                             Both
SPLAMQ   Append Multi Queue Datas                       Both
SPLGQC   Get Queue Capacity                             Both
SPLGMQ   Get Multi Queue Capacity                       Both
SPLCQD   Clear Queue Datas                              Both
SPLCMQ   Clear Multi Queue Datas                        Both
SPLSLJ   Select&Load Job                                Stop Position
SPLLJV   Load Job Values                                Stop Position

                                 MODIFICATION COMMANDS
SPMCTV   Changing Text Value                            Both (Look at the Note at Modification Commands)
SPMCBV   Changing Barcode Value                         Both (Look at the Note at Modification Commands)
SPMC2D   Changing 2D Barcode Value                      Both (Look at the Note at Modification Commands)
SPMCCV   Changing Counter Value                         Both (Look at the Note at Modification Commands)
SPMCLV   Changing Logo Value                            Both (Look at the Note at Modification Commands)
SPMCSV   Changing Selected Values                       Both (Look at the Note at Modification Commands)

                                        PRINT COMMANDS
SPPSAP   Start Automatically Print                      Stop Position
SPPSLQ   Set Print Count for Limited print              Both
SPPGLQ   Get Print Count for Limited print              Both
SPPSTP   Stop Print                                     Print Position
                                                        Print Position(without Cassette- Intermittent models)
SPPOTP   One Test Print
                                                        Stop Position(with Cassette- Intermittent models)
SPPSTA   Status of Printer                              Both

                                      GENERAL COMMANDS
SPGSUM   Send User Message to Printer                   Both
SPGGTP   Get Total Print Count                          Both


                                             ~ 130 ~
                                   Savema Printer Programming Language (Rev.12)   03/08/2022

SPGGCP   Get Current Print Count                       Both
SPGGFW   Get Firmware Version                          Both
SPGGRR   Get Remaining Ribbon                          Both
SPGGSN   Get Serial Number of Printer                  Both

                                     TRAVERSE COMMANDS
SPTSPS   Set Pack Size                                 Stop Position
SPTGPS   Get Pack Size                                 Both
SPTSPC   Set Print Count                               Stop Position
SPTGPC   Get Print Count                               Both
SPTSPP   Set Print Position                            Stop Position
SPTGPP   Get Print position                            Both
SPTSPD   Set Pack Distance From Beginning              Stop Position
SPTGPD   Get Pack Distance From Beginning              Both
SPTSPA   Set Printing Area                             Stop Position
SPTGPA   Get Printing Area                             Both
SPTSTP   Set All Traverse Parameters                   Stop Position
SPTGTP   Get All Traverse Parameters                   Both




                                             ~ 131 ~


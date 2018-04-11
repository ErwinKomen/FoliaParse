using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using FoliaParse.conv;
using System.Diagnostics;

namespace FoliaParse {
  /* -------------------------------------------------------------------------------------
   * Name:  fpConv
   * Goal:  Routines that perform the actual conversion
   * History:
   * 2/oct/2015 ERK Created
     ------------------------------------------------------------------------------------- */
  class fpConv {
    // ========================= Constants ================================================
    static String ALPINO_PROGRAM = "/vol/customopt/alpino/bin/Alpino";
    static String ALPINO_ARGS = "-fast -notk -flag treebank $dir_name end_hook=xml -parse";
    static String ALPINO_ARGS2 = "-slow -notk -flag treebank $dir_name end_hook=xml -parse";
    // ========================= Declarations local to me =================================
    private ErrHandle errHandle = new ErrHandle();
    private String loc_sDirOut = "";
    private String loc_AlpinoProgram = ALPINO_PROGRAM;
    private AlpinoReader rdAlp = null;         // Alpino reader -- access to parsed files
    private Regex regQuoted = new Regex("xmlns(\\:(\\w)*)?=\"(.*?)\"");
    private Regex regEmptyXmlns = new Regex("xmlns(\\:(\\w)*)?=\"\"");
    // ======================== Getters and setters =======================================
    public void setAlpino(String sLoc) { this.loc_AlpinoProgram = sLoc; }

    /* -------------------------------------------------------------------------------------
     * Name:        ParseOneFoliaAlpino
     * Goal:        Master routine to parse one folia.xml Dutch file (Sonar + Lassy)
     * Parameters:  sFileIn     - File to be processed
     *              sDirOut     - Directory where the output files should come
     *              sDirParsed  - Directory where already parsed Alpino files are kept
     *              bIsDebug    - Debugging mode on or off
     *              sMethod     - The processing method: 'File', 'Sentence'
     *              bKeepGarbage- Do not delete temporary files
     * History:
     * 2/oct/2015 ERK Created
     * 6/oct/2015 ERK Added @sMethod
       ------------------------------------------------------------------------------------- */
    public bool ParseOneFoliaWithAlpino(String sFileIn, String sDirOut, String sDirParsed,
        bool bIsDebug, String sMethod, bool bUseAlpReader, bool bKeepGarbage) {
      XmlDocument pdxSent = null; // The folia input sentence
      XmlNode ndxFoliaS;          // Input FoLiA format sentence
      XmlNamespaceManager nsFolia;// Namespace manager for folia input
      XmlReader rdFolia = null;
      XmlWriter wrFolia = null;
      XmlWriterSettings wrSet = null;
      util.xmlTools oXmlTools = new util.xmlTools(errHandle);
      int iSentNum;
      List<String> lstSent;       // Container for the tokenized sentences
      List<String> lstSentId;     // Container for the FoLiA id's of the tokenized sentences
      List<XmlDocument> lstAlp;   // Container for the Alpino parses
      List<String> lstAlpFile;    // Alpino file names
      bool bRemoveXmlns = true;  // Remove the XMLNS or not

      try {
        // Validate
        if (!File.Exists(sFileIn)) return false;
        // Make sure dirout does not contain a trailing /
        if (sDirOut.EndsWith("/") || sDirOut.EndsWith("\\")) 
          sDirOut = sDirOut.Substring(0, sDirOut.Length - 1);
        // Construct output file name
        String sFileOut = sDirOut + "/" + Path.GetFileName(sFileIn);
        this.loc_sDirOut = sDirOut;

        // If the output file is already there: skip it
        if (File.Exists(sFileOut)) { debug("Skip existing ["+sFileOut+"]"); return true; }


        // Try to open an alpino reader
        if (bUseAlpReader) {
          if (rdAlp == null) rdAlp = new AlpinoReader(sDirParsed, sDirOut);
          if (rdAlp == null) {
            errHandle.Status("NO alpinoreader. DirParsed=" + sDirParsed);
          } else {
            errHandle.Status("Have alpinoreader. DirParsed=" + sDirParsed);
          }
        }

        // Other initialisations
        pdxSent = new XmlDocument();
        AlpinoToPsdx objAlpPsdx = new AlpinoToPsdx(errHandle);
        objAlpPsdx.setCurrentSrcFile(sFileIn);
        PsdxToFolia objPsdxFolia = new PsdxToFolia(errHandle);
        objPsdxFolia.setCurrentSrcFile(sFileIn);
        lstSent = new List<string>();
        lstSentId = new List<string>();
        lstAlp = new List<XmlDocument>();
        lstAlpFile = new List<String>();
        wrSet = new XmlWriterSettings();
        wrSet.Indent = true;
        wrSet.NewLineHandling = NewLineHandling.Replace;
        wrSet.NamespaceHandling = NamespaceHandling.OmitDuplicates;
        // wrSet.NewLineOnAttributes = true;

        // Action depends on the processing method
        switch (sMethod) {
          case "file":
            #region method_file
            // Open the input file
            debug("Loading: " + sFileIn);
            XmlDocument pdxConv = new XmlDocument();
            pdxConv.Load(sFileIn);

            // Create a namespace mapping for the folia *source* xml document
            nsFolia = new XmlNamespaceManager(pdxConv.NameTable);
            nsFolia.AddNamespace("df", pdxConv.DocumentElement.NamespaceURI);

            // Tokenize all sentences in the Alpino input
            debug("Tokenizing...");
            ndxFoliaS = pdxConv.SelectSingleNode("./descendant::df:s[1]", nsFolia);
            while (ndxFoliaS != null) {
              // Process this node
              String sSentId = ndxFoliaS.Attributes["xml:id"].Value;
              Console.Write("tokenizing: " + sSentId + "\r");

              // Tokenize the sentence
              String sParseInput = this.foliaTokenize(ndxFoliaS, nsFolia);

              // Keep both for future
              lstSent.Add(sParseInput);
              lstSentId.Add(sSentId);

              // Try find next <s> element in the original FoLiA
              // NOTE: this is *not* necessarily a sibling
              //       because <s> elements may be in the heading or in paragraph structures
              ndxFoliaS = ndxFoliaS.SelectSingleNode("./following::df:s", nsFolia);
            }


            // Parse all tokenized sentences in one go using Alpino parser
            debug("Parsing Alpino...");
            String sAllSent = String.Join("\n", lstSent.ToArray());
              if (!parseAlpino(sAllSent, lstSent.Count, lstAlp, lstAlpFile, bIsDebug)) {
              errHandle.DoError("ParseOneFoliaWithAlpino", "Failed to perform parseAlpino");
              return false;
            }

            // Walk through all Alpino parses
            for (int i = 0; i < lstAlp.Count; i++) {
              // Get the current sentence id
              String sSentId = lstSentId[i];

              // Get this alpino parse
              XmlNode ndxAlpino = lstAlp[i].SelectSingleNode("./descendant-or-self::alpino_ds");

              // Get dummy parameter
              List<XmlNode> lDummy = new List<XmlNode>();

 
              // ALpino -> Psdx: convert the <node> structure into a psdx one with <forest> and <eTree> etc
              XmlNode ndxPsdx = objAlpPsdx.oneSent(ndxAlpino, sSentId, lstAlpFile[i], ref lDummy);
              // Psdx -> FoLiA: Convert the <forest> structure to a FoLiA sentence <s>
              XmlNode ndxFolia = objPsdxFolia.oneSent(ndxPsdx, sSentId, "", ref lDummy);

              // Insert the <syntax> node as child to the original <s> node
              ndxFoliaS = pdxConv.SelectSingleNode("./descendant::df:s[@xml:id = '" + sSentId + "']", nsFolia);
              XmlNode ndxSyntax = pdxConv.ImportNode(ndxFolia.SelectSingleNode("./descendant-or-self::syntax"), true);
              ndxFoliaS.AppendChild(ndxSyntax);

              // Check for <t> nodes under the original folia
              if (ndxFoliaS.SelectNodes("./child::t", nsFolia).Count == 0) {
                // Get all <t> nodes in the created folia
                XmlNodeList ndxTlist = ndxFolia.SelectNodes("./child::t");
                for (int j = 0; j < ndxTlist.Count; j++) {
                  // Copy this <t> node
                  XmlNode ndxOneT = pdxConv.ImportNode(ndxTlist[j], true);
                  ndxFoliaS.PrependChild(ndxOneT);
                }
              }
            }
            // Write the result
            pdxConv.Save(sFileOut);            
            break;
          #endregion
          // =============== END of method "file" =============================
          case "sentence":
            #region method_sentence
            // Open the input file
            debug("Starting: " + sFileIn);
            // Open file for XmlRead input
            rdFolia = XmlReader.Create( new StreamReader(sFileIn));
            wrFolia = XmlWriter.Create(new StreamWriter(sFileOut), wrSet);
            iSentNum = 0;
            // Walk through the input file
            while (!rdFolia.EOF && rdFolia.Read()) {
              // Check what this is
              if (rdFolia.IsStartElement("s")) {
                // Read the <s> element as one string
                String sWholeS = rdFolia.ReadOuterXml();
                iSentNum++;
                // Process the <s> element:
                // (1) put it into an XmlDocument
                XmlDocument pdxSrc = new XmlDocument();
                pdxSrc.LoadXml(sWholeS);
                // (2) Create a namespace mapping for the folia *source* xml document
                nsFolia = new XmlNamespaceManager(pdxSrc.NameTable);
                nsFolia.AddNamespace("df", pdxSrc.DocumentElement.NamespaceURI);
                // (3) Prepare tokenization
                ndxFoliaS = pdxSrc.SelectSingleNode("./descendant-or-self::df:s[1]", nsFolia);
                String sSentId = ndxFoliaS.Attributes["xml:id"].Value;
                Console.Write("s[" + iSentNum + "]: " + sSentId + "\r");
                // (4) parse the tokenized sentence or get a parse from somewhere else
                // Perform actual tokenization
                String sParseInput = this.foliaTokenize(ndxFoliaS, nsFolia);
                // Create a parse by calling the Alpino parser
                if (!parseAlpino(sParseInput, 1, lstAlp, lstAlpFile, bIsDebug)) {
                  errHandle.DoError("ParseOneFoliaWithAlpino", "Failed to perform parseAlpino");
                  return false;
                }
                if (lstAlp.Count == 0) { errHandle.DoError("ParseOneFoliaWithAlpino", "Alpino could not parse sentence"); return false; }

                // (4b) Get a list of <w> nodes under this <s>
                List<XmlNode> lstW = oXmlTools.FixList(ndxFoliaS.SelectNodes("./descendant::df:w", nsFolia));

                // (5) Retrieve the *first* alpino parse
                XmlNode ndxAlpino = lstAlp[0].SelectSingleNode("./descendant-or-self::alpino_ds");
                // (6) ALpino -> Psdx: convert the <node> structure into a psdx one with <forest> and <eTree> etc
                XmlNode ndxPsdx = objAlpPsdx.oneSent(ndxAlpino, sSentId, lstAlpFile[0], ref lstW);
                // (7) Psdx -> FoLiA: Convert the <forest> structure to a FoLiA sentence <s>
                //             Adapt the list of words inside [lstW]
                XmlNode ndxFolia = objPsdxFolia.oneSent(ndxPsdx, sSentId, "", ref lstW);

                // (8) Insert the <syntax> node as child to the original <s> node
                ndxFoliaS = pdxSrc.SelectSingleNode("./descendant-or-self::df:s[@xml:id = '" + sSentId + "']", nsFolia);
                XmlNode ndxSyntax = pdxSrc.ImportNode(ndxFolia.SelectSingleNode("./descendant-or-self::syntax"), true);
                ndxFoliaS.AppendChild(ndxSyntax);

                // (9) Copy the adaptations of the word list into the [pdxSrc] list
                oXmlTools.SetXmlDocument(pdxSrc);
                XmlNode ndxLastW = ndxFoliaS.SelectSingleNode("./descendant::df:w[last()]", nsFolia);
                for (int j = 0; j < lstW.Count; j++) {
                  // Get this element and the class
                  XmlNode ndxOneW = lstW[j]; String sClass = ndxOneW.Attributes["class"].Value;
                  // Action depends on type
                  switch (sClass) {
                    case "Zero":
                    case "Star":
                      // Relocate the new node
                      ndxLastW.ParentNode.InsertAfter(ndxOneW, ndxLastW);
                      ndxLastW = ndxOneW;
                      break;
                    default:
                      // Just add the class attribute
                      oXmlTools.AddAttribute(ndxFolia.SelectSingleNode("./descendant::df:w[@id='"+
                        ndxOneW.Attributes["xml:id"].Value+"']", nsFolia),"class", sClass);
                      break;
                  }
                }

                // (10) Check for <t> nodes under the original folia
                if (ndxFoliaS.SelectNodes("./child::t", nsFolia).Count == 0) {
                  // Get all <t> nodes in the created folia
                  XmlNodeList ndxTlist = ndxFolia.SelectNodes("./child::t");
                  for (int j = 0; j < ndxTlist.Count; j++) {
                    // Copy this <t> node
                    XmlNode ndxOneT = pdxSrc.ImportNode(ndxTlist[j], true);
                    ndxFoliaS.PrependChild(ndxOneT);
                  }
                }

                // (11) Write the new <s> node to the writer
                XmlReader rdResult = XmlReader.Create(new StringReader(ndxFoliaS.SelectSingleNode("./descendant-or-self::df:s", nsFolia).OuterXml));
                wrFolia.WriteNode(rdResult, true);
                // wrFolia.WriteString("\n");
                rdResult.Close();
              } else {
                // Just write it out
                WriteShallowNode(rdFolia, wrFolia);
              }
            }
            // Finish reading input
            rdFolia.Close();
            // Finish writing
            wrFolia.Close();
            break;
          #endregion
          // =============== END of method "sentence" =============================
          case "twopass": case "two-pass":
            #region method_twopass
            // The 'twopass' method:
            //      (1) Read the FoLiA with a stream-reader sentence-by-sentence
            //          a. Extract the sencence text (tokenize)
            //          b. Parse it using Alpino
            //          c. Save the parse in a sub-directory
            //      (2) Read the Folia with a stream-reader again sentence-by-sentence
            //          a. Read the <s> from the FoLiA
            //          b. Get a list of <w> elements in the <s>
            //          a. Retrieve the stored Alpino parse
            //          b. Alpino >> Psdx + adaptation of structure
            //          c. Psdx >> Folia 
            //          d. Insert resulting <syntax> as child under existing <s>
            //          e. Write output into streamwriter
            // Open the input file
            debug("Starting: " + sFileIn);
            iSentNum = 0;
            // Make room for the intermediate results
            String sShort = Path.GetFileNameWithoutExtension(sFileOut);
            String sFileOutDir = Path.GetDirectoryName(sFileOut) + "/" + sShort;
            String sFileOutLog = sFileOutDir + ".log";
            if (!Directory.Exists(sFileOutDir)) Directory.CreateDirectory(sFileOutDir);
            // Remove the last slash for clarity
            if (sFileOutDir.EndsWith("/") || sFileOutDir.EndsWith("\\")) 
              sFileOutDir = sFileOutDir.Substring(0, sFileOutDir.Length - 1);
            // =============== FIRST PASS ====================
            #region first_pass
            // Open only input file
            rdFolia = XmlReader.Create(new StreamReader(sFileIn));
            // Walk through the input file
            while (!rdFolia.EOF && rdFolia.Read()) {
              // Check what this is
              if (rdFolia.IsStartElement("s")) {
                // Read the <s> element as one string
                String sWholeS = rdFolia.ReadOuterXml();
                iSentNum++;
                // Calculate what the output file is going to be
                String sThisAlpFile = sFileOutDir + "/" + iSentNum + ".xml";
                // Skip if it exists
                if (!File.Exists(sThisAlpFile)) {
                  // Process the <s> element:
                  // (1) put the whole <s> into an XmlDocument
                  XmlDocument pdxSrc = new XmlDocument();
                  pdxSrc.LoadXml(sWholeS);
                  // (2) Create a namespace mapping for the folia *source* xml document
                  nsFolia = new XmlNamespaceManager(pdxSrc.NameTable);
                  nsFolia.AddNamespace("df", pdxSrc.DocumentElement.NamespaceURI);
                  // (3) tokenization
                  ndxFoliaS = pdxSrc.SelectSingleNode("./descendant-or-self::df:s[1]", nsFolia);
                  String sSentId = ndxFoliaS.Attributes["xml:id"].Value;
                  // If there is no alpino reader, we'll have to do the reading ourselves
                  if (rdAlp == null) {
                    // Parse one line of alpino
                    doOneLogLine(sFileOutLog, "s1-prs[" + iSentNum + "]: " + sSentId);
                    if (!doOneAlpinoParse(ndxFoliaS, nsFolia, ref lstAlp, ref lstAlpFile, bIsDebug, sThisAlpFile)) return false;
                  } else {
                    // If necessary, position the Alpino reader at the correct point
                    doOneLogLine(sFileOutLog, "s1-get[" + iSentNum + "]: " + sSentId);
                    if (iSentNum == 1 && !rdAlp.ready()) {
                      if (!rdAlp.findSentId(sSentId)) { errHandle.DoError("ParseOneFoliaWithAlpino", "Could not [findSentId()]: " + sSentId); return false;  }
                    }
                    // Instead of doing a parse, get the parse from the Lassy .data + .index file
                    if (!setAlpinoParse(rdAlp, sSentId, ndxFoliaS, nsFolia, ref lstAlp, ref lstAlpFile, sThisAlpFile, sFileOutLog)) {
                      // ERROR: did not receive something back 
                      errHandle.DoError("ParseOneFoliaWithAlpino", "Could not extract parse of line [" + sSentId + "]");
                      return false;
                    }
                  }
                }
              }
            }
            // Finish reading input
            rdFolia.Close();
            #endregion
            // ======= SECOND PASS ===============
            String sFileTmp = sFileOut + ".tmp";
            // Open input file and output file
            #region second_pass
            using (rdFolia = XmlReader.Create(new StreamReader(sFileIn))) {
              StreamWriter wrTmp = new StreamWriter(sFileTmp);
              using (wrFolia = XmlWriter.Create(wrTmp, wrSet)) {
                // ===========================
                //            var nsZero = new XmlSerializerNamespaces();
                //            nsZero.Add("", ""); 
                //            var serializer = new XmlSerializer(yourType); 
                //            serializer.Serialize(xmlTextWriter, someObject, ns);
                // =============================
                iSentNum = 0;
                // Walk through the input file
                #region walk_input_file
                while (!rdFolia.EOF && rdFolia.Read()) {
                  // Check what this is
                  if (rdFolia.IsStartElement("annotations")) {
                    #region treat_annotations
                    // Get the <annotations> section and adapt it
                    String sAnnot = rdFolia.ReadOuterXml();
                    // (1) Put into Xml document
                    XmlDocument pdxAnn = new XmlDocument(); pdxAnn.LoadXml(sAnnot);
                    // (2) Create a namespace mapping for the folia *source* xml document
                    nsFolia = new XmlNamespaceManager(pdxAnn.NameTable);
                    nsFolia.AddNamespace("df", pdxAnn.DocumentElement.NamespaceURI);
                    // (3) Add the Lassy syntactic annotations
                    oXmlTools.SetXmlDocument(pdxAnn);
                    ndxFoliaS = pdxAnn.SelectSingleNode("./descendant-or-self::df:annotations", nsFolia);
                    // Check if dependency is already available or not
                    XmlNode ndxDep = ndxFoliaS.SelectSingleNode("./child::df:dependency-annotation", nsFolia);
                    if (ndxDep == null) {
                      // Only add the dependency-annotation set, if this is not already there (e.g. by frogging)
                      // Note: see http://www.let.rug.nl/vannoord/Lassy/
                      oXmlTools.AddXmlChild(ndxFoliaS, "dependency-annotation",
                        "annotator", "Lassy/Alpino", "attribute",
                        "annotatortype", "auto", "attribute",
                        "set", "http://www.let.rug.nl/vannoord/Lassy/", "attribute");
                    }
                    oXmlTools.AddXmlChild(ndxFoliaS, "syntax-annotation",
                      "annotator", "Cesax/FoliaParse.exe (surfacing)", "attribute",
                      "annotatortype", "auto", "attribute",
                      "set", "foliaparse", "attribute");
                    // (10) Write the new <annotations> node to the writer
                    XmlReader rdResult = XmlReader.Create(new StringReader(ndxFoliaS.SelectSingleNode("./descendant-or-self::df:annotations", nsFolia).OuterXml));
                    wrFolia.WriteNode(rdResult, true);
                    // wrFolia.WriteString("\n");
                    wrFolia.Flush();
                    #endregion
                  } else if (rdFolia.IsStartElement("s")) {
                    #region folia_read_s
                    // Read the <s> element as one string
                    String sWholeS = rdFolia.ReadOuterXml();
                    iSentNum++;
                    // Process the <s> element:
                    // (1) put it into an XmlDocument
                    XmlDocument pdxSrc = new XmlDocument();
                    pdxSrc.LoadXml(sWholeS);
                    // (2) Create a namespace mapping for the folia *source* xml document
                    nsFolia = new XmlNamespaceManager(pdxSrc.NameTable);
                    nsFolia.AddNamespace("df", pdxSrc.DocumentElement.NamespaceURI);
                    // (3) preparations
                    ndxFoliaS = pdxSrc.SelectSingleNode("./descendant-or-self::df:s[1]", nsFolia);
                    String sSentId = ndxFoliaS.Attributes["xml:id"].Value;
                    String sLogMsg = "s2[" + iSentNum + "]: " + sSentId + "\r";
                    Console.Write(sLogMsg);
                    /* */
                    // =============== debugging ==========
                    if (sSentId == "BVws9.s.15") {
                      int iErwin = 55;
                    }
                    // ====================================
                     /* */
                    File.AppendAllText(sFileOutLog, sLogMsg);
                    #endregion

                    #region folia_get_list_of_words
                    // (3b) Get a list of <w> nodes under this <s>
                    List<XmlNode> lstW = oXmlTools.FixList(ndxFoliaS.SelectNodes("./descendant::df:w", nsFolia));

                    // (3c) To prevent structure within the <s>/<w> layer, re-number the word identifiers
                    for (int iLstW=0;iLstW < lstW.Count;iLstW++) {
                      lstW[iLstW].Attributes["xml:id"].Value = sSentId + ".w." + (iLstW + 1);
                    }
                    #endregion

                    #region alpino_read_parse
                    // (4) retrieve the parse of the tokenized sentence
                    String sAlpFile = sFileOutDir + "/" + iSentNum + ".xml";
                    if (!getAlpinoParse(sAlpFile, lstAlp, lstAlpFile)) {
                      errHandle.DoError("ParseOneFoliaWithAlpino", "Failed to perform getAlpinoParse");
                      return false;
                    }
                    if (lstAlp.Count == 0) { errHandle.DoError("ParseOneFoliaWithAlpino", "Could not retrieve parsed alpino sentence"); return false; }
                    // (5) Retrieve the *first* alpino parse
                    XmlNode ndxAlpino = lstAlp[0].SelectSingleNode("./descendant-or-self::alpino_ds");
                    #endregion

                    #region alpino_to_psdx
                    // (6) ALpino -> Psdx: convert the <node> structure into a psdx one with <forest> and <eTree> etc
                    XmlNode ndxPsdx = objAlpPsdx.oneSent(ndxAlpino, sSentId, lstAlpFile[0], ref lstW);
                    #endregion

                    #region psdx_to_folia_s
                    // (7) Psdx -> FoLiA: Convert the <forest> structure to a FoLiA sentence <s>
                    XmlNode ndxFolia = objPsdxFolia.oneSent(ndxPsdx, sSentId, "", ref lstW);
                    #endregion

                    #region folia_insert_syntax_into_s
                    // (8) Insert the <syntax> node as child to the original <s> node
                    ndxFoliaS = pdxSrc.SelectSingleNode("./descendant-or-self::df:s[@xml:id = '" + sSentId + "']", nsFolia);
                    XmlNode ndxSyntax = pdxSrc.ImportNode(ndxFolia.SelectSingleNode("./descendant-or-self::syntax"), true);
                    ndxFoliaS.AppendChild(ndxSyntax);
                    #endregion

                    #region folia_wordlist_adaptations_copy
                    // (9) Copy the adaptations of the word list into the [pdxSrc] list
                    oXmlTools.SetXmlDocument(pdxSrc);
                    XmlNode ndxLastW = ndxFoliaS.SelectSingleNode("./descendant::df:w[last()]", nsFolia);
                    for (int j = 0; j < lstW.Count; j++) {
                      // Get this element and the class
                      XmlNode ndxOneW = lstW[j];
                      // Double check if there is a class attribute
                      if (ndxOneW.Attributes["class"] == null) {
                        // Create it
                        int stop = 1;
                      }
                      String sClass = ndxOneW.Attributes["class"].Value;
                      // Action depends on type
                      switch (sClass) {
                        case "Zero":
                        case "Star":
                          // Relocate the new node
                          ndxLastW.ParentNode.InsertAfter(ndxOneW, ndxLastW);
                          ndxLastW = ndxOneW;
                          break;
                        default:
                          // Adding the class attribute is not needed: it is already done in the list
                          //oXmlTools.AddAttribute(ndxFoliaS.SelectSingleNode("./descendant::df:w[@xml:id='" +
                          //  ndxOneW.Attributes["xml:id"].Value + "']", nsFolia), "class", sClass);
                          break;
                      }
                    }
                    #endregion

                    #region folia_adapt_t_nodes
                    // (9) Check for <t> nodes under the original folia
                    if (ndxFoliaS.SelectNodes("./child::t", nsFolia).Count == 0) {
                      // Get all <t> nodes in the created folia
                      XmlNodeList ndxTlist = ndxFolia.SelectNodes("./child::t");
                      for (int j = 0; j < ndxTlist.Count; j++) {
                        // Copy this <t> node
                        XmlNode ndxOneT = pdxSrc.ImportNode(ndxTlist[j], true);
                        ndxFoliaS.PrependChild(ndxOneT);
                      }
                    }
                    #endregion

                    #region folia_write_output
                    // (10) Write the new <s> node to the writer
                    XmlReader rdResult = XmlReader.Create(new StringReader(ndxFoliaS.SelectSingleNode("./descendant-or-self::df:s", nsFolia).OuterXml));
                    wrFolia.WriteNode(rdResult, true);
                    #endregion

                    wrFolia.Flush();
                    rdResult.Close();
                  } else {
                    // Just write it out
                    WriteShallowNode(rdFolia, wrFolia);
                  }
                }
                #endregion
                // Finish reading input
                rdFolia.Close();
                // Finish writing
                wrFolia.Flush();
                wrFolia.Close();
                wrFolia = null;
              }
              wrTmp.Close();
              wrTmp = null;
            }
            #endregion
            // ======= THIRD PASS ===============
            // Open input file and output file
            #region third_pass
            using (StreamReader rdText = new StreamReader(sFileTmp)) {
              using (StreamWriter wrText = new StreamWriter(sFileOut)) {
                String sLine;
                while ((sLine = rdText.ReadLine()) != null) {
                  if (bRemoveXmlns) {
                    // Remove All xmlns
                    sLine = regQuoted.Replace(sLine, "");
                    if (sLine.Contains("<FoLiA")) {
                      sLine = sLine.Replace("<FoLiA", "<FoLiA xmlns:xlink=\"http://www.w3.org/1999/xlink\" xmlns=\"http://ilk.uvt.nl/folia\" ");
                    }
                  }
                  // Write the result
                  wrText.WriteLine(sLine);
                }
              }
            }
            #endregion

            //  Garbage collection
            #region garbage_collection
            if (!bKeepGarbage) {
              // Clean-up: remove temporary files as well as temporary directory
              Directory.Delete(sFileOutDir, true);
              // Clean-up: remove the .log file
              File.Delete(sFileOutDir + ".log");
              // Remove temporary file
              File.Delete(sFileTmp);
            }
            #endregion
            break;
            #endregion
        }


        // Be positive
        return true;
      } catch (Exception ex) {
        errHandle.DoError("ParseOneFoliaWithAlpino", ex); // Provide standard error message
        return false;        
        throw;
      }
    }

    /// <summary>
    /// Write one message line to console and to log file
    /// </summary>
    /// <param name="sFileOutLog"></param>
    /// <param name="sLogMsg"></param>
    private void doOneLogLine(String sFileOutLog, String sLogMsg) {
      errHandle.Status(sLogMsg+"\n");
      File.AppendAllText(sFileOutLog, sLogMsg + "\n");
    }

    /* -------------------------------------------------------------------------------------
      * Name:        doOneAlpinoParse
      * Goal:        Parse exactly one line using Alpino
      * Parameters:  sFileIn     - File to be processed
      *              sDirOut     - Directory where the output files should come
      *              sDirParsed  - Directory where already parsed Alpino files are kept
      *              bIsDebug    - Debugging mode on or off
      *              sMethod     - The processing method: 'File', 'Sentence'
      *              bKeepGarbage- Do not delete temporary files
      * History:
      * 26/oct/2015 ERK Created
        ------------------------------------------------------------------------------------- */
    private bool doOneAlpinoParse(XmlNode ndxFoliaS, XmlNamespaceManager nsFolia, ref List<XmlDocument> lstAlp,
      ref List<String> lstAlpFile, bool bIsDebug, String sThisAlpFile) {
      int iDebugLevel = 1;

      try {
        // Do tokenization that delivers something like e.g:
        //   [ @folia ik VNW(pers,pron,nomin,vol,1,ev) Ik ] [ @folia zijn WW(pv,tgw,ev) ben ] ...
        String sParseInput = this.foliaTokenize(ndxFoliaS, nsFolia, "folia");
        // Debugging: show the parse
        if (iDebugLevel>0) {
          errHandle.Status("Parser input:\n" + sParseInput);
        }
        // (5) parse the tokenized sentence
        if (!parseAlpino(sParseInput, 1, lstAlp, lstAlpFile, bIsDebug)) {
          errHandle.DoError("ParseOneFoliaWithAlpino", "Failed to perform parseAlpino");
          return false;
        }
        // ================ DEBUGGING ====================
        if (iDebugLevel > 0) 
          errHandle.Status("returned correctly from parseAlpino. lstAlp="+lstAlp.Count+" lstAlpFile="+lstAlpFile.Count);
        // ===============================================
        // Check if we received something back
        if (lstAlp.Count == 0) {
          // ERROR: did not receive something back 
          errHandle.DoError("ParseOneFoliaWithAlpino", "Alpino failed to parse [" + sParseInput + "]");
          return false;
        }
        // Copy the alpino file to the subdirectory
        File.Copy(lstAlpFile[0], sThisAlpFile, true);

        return true;
      } catch (Exception ex) {
        errHandle.DoError("doOneAlpinoParse", ex); // Provide standard error message
        return false;
      }
    }

    /* -------------------------------------------------------------------------------------
     * Name:  foliaTokenize
     * Goal:  Tokenize the FoLiA sentence
     * History:
     * 2/oct/2015   ERK Created
     * 26/oct/2015  ERK Added @sType
       ------------------------------------------------------------------------------------- */
    private String foliaTokenize(XmlDocument pdxSent, XmlNamespaceManager nsFolia, String sType = "bare") {
      // Get all the words in this sentence
      XmlNodeList ndxList = pdxSent.SelectNodes("//df:w", nsFolia);
      return foliaTokenize(ndxList, nsFolia, sType);
    }
    private String foliaTokenize(XmlNode ndxSent, XmlNamespaceManager nsFolia, String sType = "bare") {
      // Get all the words in this sentence
      XmlNodeList ndxList = ndxSent.SelectNodes("./descendant-or-self::df:w", nsFolia);
      return foliaTokenize(ndxList, nsFolia, sType);
    }
    private String foliaTokenize(XmlNodeList ndxList, XmlNamespaceManager nsFolia, String sType = "bare") {
      try {
        // Validate
        if (ndxList == null) {
          // Should not have happened
          errHandle.DoError("foliaTokenize", "The input list is empty"); 
        }
        // Put the words into a list, as well as the identifiers
        List<String> lstWords = new List<string>();
        List<String> lstWrdId = new List<string>();
        for (int i = 0; i < ndxList.Count; i++) {
          lstWords.Add(ndxList.Item(i).SelectSingleNode("./child::df:t", nsFolia).InnerText);
          lstWrdId.Add(ndxList.Item(i).Attributes["xml:id"].Value);
        }
        // errHandle.Status("foliaTokenize point #1");
        // Combining the words together depends on @sType
        String sBack = "";
        // errHandle.Status("foliaTokenize #1");
        switch (sType) {
          case "bare":
            // Combine identifier and words into sentence
            sBack = String.Join(" ", lstWords.ToArray());
            break;
          case "folia":
            // Combine identifier and words into sentence
            StringBuilder sb = new StringBuilder();
            // errHandle.Status("foliaTokenize #2: " + lstWords.Count);
            for (int i = 0; i < lstWords.Count; i++) {
              //errHandle.Status("foliaTokenize point #3a, i=" + i);
              XmlNode ndxW = ndxList.Item(i);
              //errHandle.Status("foliaTokenize point #3b, i=" + i);
              // Get the 'class' attribute of the word to check for emoticons
              XmlAttribute ndxWclass = ndxW.Attributes["class"];
              String sWrdClass = "";
              //errHandle.Status("foliaTokenize point #3c, i=" + i);
              if (ndxWclass == null) {
                sWrdClass = "";
              } else {
                sWrdClass = ndxW.Attributes["class"].Value;
              }
              // errHandle.Status("foliaTokenize point #3d, i=" + i);
              if (sWrdClass.ToLower() == "emoticon") {
                sb.Append("[ @folia x emoji x ] ");
              } else if (sWrdClass.ToLower() == "symbol") {
                sb.Append("[ @folia x symbol x ] ");
              } else if (sWrdClass.ToLower() == "pictogram") {
                sb.Append("[ @folia x pictogram x ] ");
              } else if (sWrdClass.ToLower() == "unknown") {
                sb.Append("[ @folia x unknown x ] ");
              } else if (sWrdClass.ToLower() == "reverse-smiley") {
                sb.Append("[ @folia x rsmiley x ] ");
              } else if (sWrdClass.ToLower() == "smiley") {
                sb.Append("[ @folia x smiley x ] ");
              } else {
                // errHandle.Status("foliaTokenize point #3e, i=" + i);
                // errHandle.Status("foliaTokenize #3: " + ((ndxW == null) ? "null" : "ok"));
                String sPosTag = "";
                XmlNode ndxPos = ndxW.SelectSingleNode("./child::df:pos", nsFolia);
                if (ndxPos != null) {
                  sPosTag = ndxPos.Attributes["class"].Value;
                }

                XmlNode ndxLemma = ndxW.SelectSingleNode("./child::df:lemma", nsFolia);
                String sLemma = "";
                if (ndxLemma != null) {
                  sLemma = ndxLemma.Attributes["class"].Value;
                }
                // Make sure a word does *NOT* contain spaces!!
                String sWord = lstWords[i].Replace(" ", "");
                // Remove non-printing characters from [sWord] and [sLemma]
                sWord = Regex.Replace(sWord, @"\p{Cs}", "");
                sLemma = Regex.Replace(sLemma, @"\p{Cs}", "");
                // Add this line
                if (sLemma == "" && sPosTag == "") {
                  sb.Append(sWord + " ");
                } else {
                  sb.Append("[ @folia " + sLemma + " " + sPosTag + " " + sWord + " ] ");
                }
              }
            }
            sBack = sb.ToString();
            break;
        }
        // Return the result
        return sBack;
      } catch (Exception ex) {
        errHandle.DoError("foliaTokenize ("+sType+")", ex); // Provide standard error message
        return "";
        throw;
      }
    }

    /* -------------------------------------------------------------------------------------
     * Name:  parseAlpino
     * Goal:  Convert a tokenized string into a <node> structure a la Alpino
     * History:
     * 2/oct/2015 ERK Created
       ------------------------------------------------------------------------------------- */
    private bool parseAlpino(String sTokSent, int iCount, List<XmlDocument> lstAlpino, 
      List<String> lstAlpFile, bool bIsDebug) {
      XmlDocument pdxThis = new XmlDocument();
      String[] arFiles;
      int iDebugLevel = 1;

      try {
        // Initialize results list
        lstAlpino.Clear();
        lstAlpFile.Clear();

        // Create temporary output directory
        String sTmpDir = this.loc_sDirOut + "/tmp";
        sTmpDir = sTmpDir.Replace("\\", "/");
        if (!Directory.Exists(sTmpDir)) Directory.CreateDirectory(sTmpDir);

        // Actual Alpino parsing should only be done if this is no debugging
        if (!bIsDebug) {
          // Look for files in this directory and delete them
          arFiles = Directory.GetFiles(sTmpDir, "*.xml");
          for (int i = 0; i < arFiles.Length; i++) {
            // Delete this file
            File.Delete(arFiles[i]);
          }
          // Create a process to run
          ProcessStartInfo startInfo = new ProcessStartInfo();
          startInfo.FileName = this.loc_AlpinoProgram;

          startInfo.Arguments = ALPINO_ARGS.Replace("$dir_name", sTmpDir);
          startInfo.UseShellExecute = false;
          startInfo.RedirectStandardInput = true;
          startInfo.ErrorDialog = false;
          startInfo.RedirectStandardError = true;
          // ============ DEBUG ==========
          // debug("parseAlpino: [" + sTokSent + "]");
          if (iDebugLevel>0) {
            debug("Command to alpino: \n" + this.loc_AlpinoProgram + " " + startInfo.Arguments + "\n");
          }
          // =============================

          // Start the program *synchronously*
          using (Process prThis = Process.Start(startInfo)) {
            // Show which process is being used
            debug("ALP process: " + prThis.Id + " name=" + prThis.ProcessName);
            // Set the standard input to the tokens
            StreamWriter swStdIn = prThis.StandardInput;
            // Write our sentence to the standard input
            swStdIn.WriteLine(sTokSent);
            swStdIn.Close();
            // Wait for its completion
            prThis.WaitForExit();
            // ============ DEBUG ==========
            debug("ALP-a[" + prThis.ExitCode + "]\n");
            // =============================
          }
          // Check if ANY .xml files have been produced
          if (Directory.GetFiles(sTmpDir, "*.xml").Length == 0) {
            // Try an alternative: use -slow instead of -fast
            startInfo.Arguments = ALPINO_ARGS2.Replace("$dir_name", sTmpDir);
            // Start the program *synchronously*
            using (Process prThis = Process.Start(startInfo)) {
              // Set the standard input to the tokens
              StreamWriter swStdIn = prThis.StandardInput;
              // Write our sentence to the standard input
              swStdIn.WriteLine(sTokSent);
              swStdIn.Close();
              // Wait for its completion
              prThis.WaitForExit();
              // ============ DEBUG ==========
              debug("ALP-b[" + prThis.ExitCode + "]\n");
              // =============================
            }
            // Again check the outcome
            if (Directory.GetFiles(sTmpDir, "*.xml").Length > 0) {
              // Re-name the very first one
              String sFirst = Directory.GetFiles(sTmpDir, "*.xml")[0];
              File.Copy(sFirst, sTmpDir + "/1.xml",true);
            }
          }
        }

        // New method: look for what we expect and look for it in the correct order
        for (int i=0;i<iCount;i++) {
          // Create the file name
          String sFileOut = sTmpDir + "/" + (i+1) + ".xml";
          // Check if file exists
          if (File.Exists(sFileOut)) {
            // Read this file into an XmlDocument structure
            String sParse = File.ReadAllText(sFileOut, System.Text.Encoding.UTF8);
            pdxThis = new XmlDocument();
            // Convert to xml
            pdxThis.LoadXml(sParse);
            // Add this to the list
            lstAlpino.Add(pdxThis);
            lstAlpFile.Add(sFileOut);
          } else {
            lstAlpino.Add(null);
            lstAlpFile.Add(sFileOut);
            debug("parseAlpino skip [" + (i + 1) + ".xml]\n");
          }
        }
 
        // Return the result
        return true;
      } catch (Exception ex) {
        errHandle.DoError("parseAlpino", ex); // Provide standard error message
        return false;        
        throw;
      }
    }

    /* -------------------------------------------------------------------------------------
     * Name:  setAlpinoParse
     * Goal:  Retrieve the sentence with [sSentId] from the random-access stream [fFileAlpino]
     *          Make use of the information in [rdAlp]
     *          Save the result in [sThisAlpFile]
     * History:
     * 2/oct/2015 ERK Created
       ------------------------------------------------------------------------------------- */
    private bool setAlpinoParse(AlpinoReader rdThis, String sSentId, XmlNode ndxFoliaS, XmlNamespaceManager nsFolia, 
      ref List<XmlDocument> lstAlp, ref List<String> lstAlpFile, String sThisAlpFile, String sFileOutLog) {
      FileStream fFileAlpino = null;
      AlpIndexEl elIndex = null;
      bool bFound = false;

      try {
        // Try read the next sentence
        bFound = rdThis.nextAlpinoIndex(sSentId, ref fFileAlpino, ref elIndex);
        if (!bFound) {
          // Try to find the sentence by looking through *all* index files
          if (rdThis.findSentId(sSentId)) {
            // Read this sentence
            bFound = rdThis.nextAlpinoIndex(sSentId, ref fFileAlpino, ref elIndex);
          }  
          if (!bFound) {
            // perform a real Alpino parse of this missing line
            doOneLogLine(sFileOutLog, "setAlpinoParse: do real parse for: " + sSentId);
            if (!doOneAlpinoParse(ndxFoliaS, nsFolia, ref lstAlp, ref lstAlpFile, false, sThisAlpFile)) return false;
            // Show what we have done
            doOneLogLine(sFileOutLog, "setAlpinoParse: found own parse of line: " + sSentId);
            // Return positively
            return true;
          }
        }
        // Read the part that is required
        var buffer = new byte[elIndex.size];
        try {
          fFileAlpino.Seek(elIndex.start, SeekOrigin.Begin);
          int bytesRead = fFileAlpino.Read(buffer, 0, buffer.Length);
        } catch (Exception ex) {
          errHandle.DoError("setAlpinoParse", ex); // Provide standard error message
          return false;
        }
        // Convert array of bytes into string
        String sAlpChunk = System.Text.Encoding.UTF8.GetString(buffer);

        // Write the string to the indicated file in UTF8
        File.WriteAllText(sThisAlpFile, sAlpChunk, System.Text.Encoding.UTF8);
        // SUccess
        return true;
      } catch (Exception ex) {
        errHandle.DoError("setAlpinoParse", ex); // Provide standard error message
        return false;        
      }
    }

    /* -------------------------------------------------------------------------------------
     * Name:  getAlpinoParse
     * Goal:  Retrieve one alpino parse from the indicated file
     * History:
     * 2/oct/2015 ERK Created
       ------------------------------------------------------------------------------------- */
    private bool getAlpinoParse(String sFileName, List<XmlDocument> lstAlpino,
      List<String> lstAlpFile) {
      XmlDocument pdxThis = new XmlDocument();


      try {
        // Initialize results list
        lstAlpino.Clear();
        lstAlpFile.Clear();

        // Check if file exists
        if (File.Exists(sFileName)) {
          // Read this file into an XmlDocument structure
          String sParse = File.ReadAllText(sFileName, System.Text.Encoding.UTF8);
          pdxThis = new XmlDocument();
          // Convert to xml
          pdxThis.LoadXml(sParse);
          // Add this to the list
          lstAlpino.Add(pdxThis);
          lstAlpFile.Add(sFileName);
        }

        // Return the result
        return true;
      } catch (Exception ex) {
        errHandle.DoError("getAlpinoParse", ex); // Provide standard error message
        return false;
        throw;
      }
    }


    /* -------------------------------------------------------------------------------------
     * Name:  WriteShallowNode
     * Goal:  Copy piece-by-piece
     * History:
     * 2/oct/2015 ERK Created
       ------------------------------------------------------------------------------------- */
    static void WriteShallowNode(XmlReader reader, XmlWriter writer) {
      if (reader == null) {
        throw new ArgumentNullException("reader");
      }
      if (writer == null) {
        throw new ArgumentNullException("writer");
      }
      switch (reader.NodeType) {
        case XmlNodeType.Element:
          writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
          writer.WriteAttributes(reader, true);
          if (reader.IsEmptyElement) {
            writer.WriteEndElement();
          }
          break;
        case XmlNodeType.Text:
          writer.WriteString(reader.Value);
          break;
        case XmlNodeType.Whitespace:
        case XmlNodeType.SignificantWhitespace:
          writer.WriteWhitespace(reader.Value);
          break;
        case XmlNodeType.CDATA:
          writer.WriteCData(reader.Value);
          break;
        case XmlNodeType.EntityReference:
          writer.WriteEntityRef(reader.Name);
          break;
        case XmlNodeType.XmlDeclaration:
        case XmlNodeType.ProcessingInstruction:
          writer.WriteProcessingInstruction(reader.Name, reader.Value);
          break;
        case XmlNodeType.DocumentType:
          writer.WriteDocType(reader.Name, reader.GetAttribute("PUBLIC"), reader.GetAttribute("SYSTEM"), reader.Value);
          break;
        case XmlNodeType.Comment:
          writer.WriteComment(reader.Value);
          break;
        case XmlNodeType.EndElement:
          writer.WriteFullEndElement();
          break;
      }

    }

    /// <summary>
    /// Write a debugging message on the console
    /// </summary>
    /// <param name="sMsg"></param>
    static void debug(String sMsg) { Console.WriteLine(sMsg); }

    /// <summary>
    /// Find the Lassy-equivalent name of the [sFileIn] FoLiA SoNaR file
    /// Make use of the .index files in directory sDirParsed
    /// </summary>
    /// <param name="sFileIn"></param>
    /// <param name="sDirParsed"></param>
    /// <returns></returns>
    static String getLassyName(String sFileIn, String sDirParsed) {
      String sBack = "";

      try {
        // Validate
        if (sFileIn == "" || sDirParsed == "" || !Directory.Exists(sDirParsed)) return "";
        // Get the bare name
        String sName = Path.GetFileName(sFileIn).Replace(".folia.xml", "");
        // Visit all .index files in parsed directory
        String[] arIndexFile = Directory.GetFiles(sDirParsed, "*.index", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < arIndexFile.Length; i++) {
          // Read the first line of this file
          String sFirst = File.ReadLines(arIndexFile[i]).First();
          // Find the part before the first '.'
          int iDot = sFirst.IndexOf('.');
          if (iDot < 0) return "";
          String sNameThis = sFirst.Substring(0, iDot );
          // Check if this is the correct name
          if (sNameThis == sName) {
            sBack = Path.GetFileName(arIndexFile[i]).Replace(".index", ""); break; 
          }
        }

        // Return what we found
        return sBack;
      } catch (Exception ex) {
        ErrHandle.HandleErr("getLassyName", ex);
        return "";
      }
    }
  }
}


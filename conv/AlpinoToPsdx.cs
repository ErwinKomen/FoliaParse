using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace FoliaParse.conv {
  /* -------------------------------------------------------------------------------------
   * Name:  AlpinoToPsdx
   * Goal:  The "XmlConv" implementation to convert Alpino <node> structure to the 
   *          psdx <forest>, <eTree>, <eLeaf> structure
   * History:
   * 2/oct/2015 ERK Created
     ------------------------------------------------------------------------------------- */
  public class AlpinoToPsdx : XmlConv {
    // ============== Local constants ==========================================
    private String strDefault = "<TEI><teiHeader><fileDesc>\n" +
      "<publicationStmt distributor='Radboud University Nijmegen' />\n" +
      "<titleStmt title='' author='' />" + "<sourceDesc bibl='' />\n" +
      "</fileDesc>" + "<profileDesc>\n" +
      "<langUsage><language ident='' name='' />\n" +
      "<creation original='' manuscript='' subtype='' genre='' /></langUsage>\n" +
      "</profileDesc>\n" +
      "</teiHeader>\n" +
      "<forestGrp File=''>\n" +
      "</forestGrp></TEI>\n";
    // ============ Local variables ============================================
    int loc_intEtreeId = 0;
    bool loc_bVersionOkay = false;
    XmlDocument pdxPsdx;
    private String sAlpFile;
    // ============ Class initializer calls the base class =====================
    public AlpinoToPsdx(ErrHandle objErr) { 
      this.errHandle = objErr; 
      this.oXmlTools = new util.xmlTools(objErr);
      this.oPsdxTools = new util.psdxTools(objErr, null);
    }

    // ===================== getters and setters ==========================================
    public override String getCurrentSrcFile() {return this.sCurrentSrcFile;}
    public override void setCurrentSrcFile(String sFile) { this.sCurrentSrcFile = sFile; }

    /* -------------------------------------------------------------------------------------
     * Name:  oneAlpinoToPsdx
     * Goal:  Convert one sentence from Alpino to psdx
     * History:
     * 2/oct/2015 ERK Created
       ------------------------------------------------------------------------------------- */
    public override XmlNode oneSent(XmlNode ndxAlpino, String sSentId, String sArg, ref List<XmlNode> lWords) {
      XmlNode ndxForGrp;
      XmlNode ndxBack = null;

      try {
        // Psdx conversion specific: need to create a bare <TEI> structure
        pdxPsdx = new XmlDocument();
        pdxPsdx.LoadXml(strDefault);

        // Interpret [sArg]
        this.sAlpFile = sArg;

        // Reset the Psdx Id counting
        loc_intEtreeId = 0;

        // Set the namespace manager for this particular [pdxSrc]
        XmlDocument pdxSrc = ndxAlpino.OwnerDocument;
        XmlNamespaceManager nmsAlpinoa = new XmlNamespaceManager(ndxAlpino.OwnerDocument.NameTable);
        nmsAlpinoa.AddNamespace("alpino", ndxAlpino.OwnerDocument.DocumentElement.NamespaceURI);

        // Derive the necessary parameters from the current source file name
        String strShort = System.IO.Path.GetFileName(this.sCurrentSrcFile);
        String strTextId = System.IO.Path.GetFileNameWithoutExtension(strShort);
        String strSectId = "";  // The alpino does not come with section indicators
        // The forestid should be derived from the sentence id
        if (!util.General.IsNumeric(sSentId)) {
          // Take the last part of the sentence as far as it is numeric
          sSentId = sSentId.Substring(sSentId.LastIndexOf(".") + 1);
        }
        int intForestId = Convert.ToInt32(sSentId);

        // Get the <forestGrp> element of the target
        ndxForGrp = pdxPsdx.SelectSingleNode("./descendant-or-self::forestGrp");

        // Get the sentence from the source Alpino
        XmlNode ndxSent = ndxAlpino.SelectSingleNode("./descendant-or-self::alpino:sentence", nmsAlpinoa);
        if (ndxSent == null) {
          errHandle.DoError("AlpinoToPsdx/oneSent", "Could not find <sentence> within alpino source");
          return null;
        }
        String strSent = ndxSent.InnerText;

        // ============ DEBUG ==========
        // debug("AlpinoToPsdx: processing [" + sSentId + "]");
        // =============================

        // Call the conversion routine
        if (!OneAlpinoToPsdxForest(ref ndxForGrp, ref pdxSrc, ref ndxBack, ref intForestId, 
          strTextId, strShort, ref strSectId, strSent, nmsAlpinoa)) {
            errHandle.DoError("AlpinoToPsdx/oneSent", "Failed to complete OneAlpinoToPsdxForest");
          return null;
        }

        // ============ DEBUG ==========
        // debug("AlpinoToPsdx: positive (non-null) return from OneAlpinoToPsdxForest");
        // =============================


        // Return the result
        
        return ndxBack;
      } catch (Exception ex) {
        errHandle.DoError("AlpinoToPsdx/oneSent", ex); // Provide standard error message
        return null;
        throw;
      }
    }

    /* -------------------------------------------------------------------------------------
     * Name:  stringToSent
     * Goal:  Create an XML sentence from the string [sSent]
     * History:
     * 2/oct/2015 ERK Created
       ------------------------------------------------------------------------------------- */
    public override XmlNode stringToSent(String sSent) {
      return null;
    }


    //----------------------------------------------------------------------------------------
    // Name:       OneAlpinoToPsdxForest()
    // Goal:       Process one alpino top-level <node> element into a psdx <forest>
    // Arugments:
    //    ndxForGrp   - the <forestGroup> parent under which the new <forest> should come
    //    pdxSrc      - the Alpino source XmlDocument
    //    intForestId - the @forestId value this new Psdx sentence gets; this routine should adapt it
    //    strTextId   - the value for attribute @TextId
    //    strShort    - the short file name associated with the text
    //    strSectId   - the identifier of this section
    //    strSent     - the sentence as a whole string 
    // History:
    // 01-07-2015  ERK Created
    //----------------------------------------------------------------------------------------
    private bool OneAlpinoToPsdxForest(ref XmlNode ndxForGrp, ref XmlDocument pdxSrc, ref XmlNode ndxFor, ref int intForestId, 
      string strTextId, string strShort, ref string strSectId, string strSent, XmlNamespaceManager nmsAlp)  {
      XmlNode ndxThis = null;     // Working node
      // XmlNode ndxFor = null;      // Forest
      XmlNode ndxEtree = null;    // Etree node
      XmlNode ndxLeaf = null;     // Eleaf node
      XmlNodeList ndxList = null; // List of items
      List<XmlNode> ndxFixed;     // Fixed list of xml nodes
      XmlNode ndxWord = null;     // Word in source
      XmlDocument pdxTarget;      // The target document
      String[] arPos = {"pt", "postag", "pos"};
      // string strWord = null;      // Word
      // string strEng = null;       // The <t> node taken for the english BT
      string strPos = null;       // POS
      string strType = null;      // Type
      string strValue = null;     // Value
      int intIchCounter = 0;      // *ICH* node counter
      int intI = 0;               // Counter

      try
      {
        // Validate
        if ((ndxForGrp == null) || (pdxSrc == null)) return false;
        // Get the target document
        pdxTarget = ndxForGrp.OwnerDocument;
        oPsdxTools.setCurrentFile(pdxTarget);
        oXmlTools.SetXmlDocument(pdxTarget);
        XmlDocument pdxCurrentFile = pdxTarget;

        // Create a namespace manager for the target Psdx
        XmlNamespaceManager nmsPsdx = new XmlNamespaceManager(pdxTarget.NameTable);
        nmsPsdx.AddNamespace("psdx", pdxTarget.DocumentElement.NamespaceURI);

        // Create a new <forest> element and add its properties
        ndxFor = oXmlTools.AddXmlChild(ndxForGrp, "forest", 
          "forestId", intForestId.ToString(), "attribute", 
          "TextId", strTextId, "attribute", 
          "File", strShort + ".psdx", "attribute", 
          "Location", strTextId + "." + intForestId.ToString("0000"), "attribute");

        // Do we have a section id?
        if (!string.IsNullOrEmpty(strSectId) || intForestId == 1) {
          // Add it
          oXmlTools.AddAttribute(ndxFor, "Section", strSectId);
          // Reset id
          strSectId = "";
        }
        // Adapt the forestId value for our own usage
        intForestId++;
        // strEng = "";
        intIchCounter = 0;

        // Add the <divs>: one for the original (org) and one for English (to be added)
        ndxThis = oXmlTools.AddXmlChild(ndxFor, "div", "lang", "org", "attribute", "seg", strSent, "child");
        ndxThis = oXmlTools.AddXmlChild(ndxFor, "div", "lang", "eng", "attribute", "seg", "", "child");
        // Get all the <node> elements with attribute @word, but sorted according to @begin
        var ndSorted = pdxSrc.SelectNodes("./descendant-or-self::alpino:node[@word]", 
          nmsAlp).Cast<XmlNode>().OrderBy((ndNode) => 
            Convert.ToInt32(ndNode.Attributes["begin"].Value));

        // Walk through the nodes (which are now ordered)
        intI = 1;
        foreach (XmlNode ndxThisWithinLoop in ndSorted) {
          ndxThis = ndxThisWithinLoop;
          String sPosTag = "unknown";
          // Look for postag in preferntial order
          for (int j = 0; j < arPos.Length; j++) {
            if (ndxThis.Attributes[arPos[j]] != null) {
              sPosTag = ndxThis.Attributes[arPos[j]].Value;
              // Possibly go upper-case
              if (sPosTag.IndexOf("(") < 0) sPosTag = sPosTag.ToUpper();
              break;
            }
          }
          /*
          // Check if this is the right version of Alpino
          if (ndxThis.Attributes["postag"] == null) {
            if (!this.loc_bVersionOkay ) {
              // This is NOT the right version of alpino...
              errHandle.DoError("OneAlpinoToPsdxForest",
                "Sorry, this is *not* the right (latest) version of Alpino" +
                "\nThe correct version should have attribute @pt and/or @postag on the @word level\n"+
                "Mismatching line:\n" + ndxThisWithinLoop.OuterXml);
              return false;
            } else {
              // Look for another tag
              sPosTag = (ndxThis.Attributes["pos"] == null) ? "unknown" :  ndxThis.Attributes["pos"].Value;
            }
          } else {
            sPosTag = ndxThis.Attributes["postag"].Value;
            this.loc_bVersionOkay = true;
          }*/
          // Default values
          strType = "Vern";
          // Check for potential problem
          if (ndxThis == null) { /* Stop: */ return false; }
          // Get the word into the variable [strValue]
          strValue = ndxThis.Attributes["word"].Value;
          // Check if this is punctuation or not
          if (ndxThis.Attributes["pos"].Value == "punct") {
            // we have punctuation
            strType = "Punct";
            strPos = "";
            // Determine the value
            switch (strValue) {
              case "(": case "[":
                strValue = "(";
                break;
              case ")": case "]":
                strValue = ")";
                break;
              case ".": case "!": case "?":
                strValue = ".";
                break;
              case ",": case ":": case ";": case "-":
                strValue = ",";
                break;
              case "\"": case "'": case "«": case "»": case "''": case "``": case "/":
                strValue = "\"";
                break;
            }
          } else {
            // We have something else - determine the POS
            String sRel = ndxThis.Attributes["rel"].Value;
            if (util.General.DoLike(sRel, "hd|--"))
              strPos = sPosTag;
            else
              strPos = sPosTag + "-" + sRel.ToUpper();
            // No longer needed: strPos = strPos.ToUpper();
          }
          // We are CREATING, not synchronizing: add one XML child under [ndxFor]
          if ((strType == "Punct") && (!string.IsNullOrEmpty(strValue)))
            ndxEtree = oPsdxTools.AddEtreeChild(ref ndxFor, loc_intEtreeId, strValue, 0, 0);
          else
            ndxEtree = oPsdxTools.AddEtreeChild(ref ndxFor, loc_intEtreeId, strPos, 0, 0);
          // Keep track of the <eTree> id
          loc_intEtreeId += 1;
          // Add <eLeaf> to the <eTree> node
          ndxLeaf = oPsdxTools.AddEleafChild(ref ndxEtree, strType, strValue, 0, 0);
          if (ndxLeaf == null) { errHandle.DoError("modConvert/OneAlpinoToPsdxForest", "Could not create <eLeaf>"); return false; }
          // The @n attribute is the number of the word inside the current sentence (=forest)
          ndxLeaf.Attributes["n"].Value = intI.ToString();
          intI += 1;
          // Walk through all the attributes
          foreach (XmlAttribute attrThis in ndxThis.Attributes) {
            // Check if it is okay
            switch (attrThis.Name) {
              /* case "postag": case "rel": case "word": */
              case "postag": case "word":
                // No action
                break;
              case "lemma":
                // Add this separately
                oPsdxTools.AddFeature(ref pdxCurrentFile, ref ndxEtree, "M", "l", attrThis.Value);
                break;
              default:
                // add the name and the value
                oPsdxTools.AddFeature(ref pdxCurrentFile, ref ndxEtree, "alp", attrThis.Name, attrThis.Value);
                break;
            }
          }
        }
    
        // Merk all non-word nodes 
        ndxList = pdxSrc.SelectNodes("./descendant-or-self::alpino:node[not(@word)]", nmsAlp);

        for (intI = 0; intI < ndxList.Count; intI++) {
          XmlNode ndxCurrent = ndxList[intI];
          oXmlTools.AddAttribute(pdxSrc, ref ndxCurrent, "done", "no");
        }
        // TODO: perhaps delete this statement, 
        //       since we already start out by getting the current file from oXmlTools
        oXmlTools.SetXmlDocument(pdxCurrentFile, "");

        // Process the words one-by-one, but they should be in a *fixed* list!
        ndxFixed = oXmlTools.FixList(ndxFor.SelectNodes("./child::psdx:eTree", nmsPsdx));
        foreach (XmlNode ndxThisWithinLoop in ndxFixed) {
          // Separate the current item
          XmlNode ndxListItem = ndxThisWithinLoop;

          // Find this node in the Alpino source [pdxSrc]
          ndxWord = pdxSrc.SelectSingleNode("./descendant::alpino:node[@id='" +
            oPsdxTools.GetFeature(ndxListItem, "alp", "id") + "']", nmsAlp);
          // Get the parent of this node
          XmlNode ndxParent = null;
          string strLabel = null;

          // Start going upwards in the Alpino source tree
          ndxParent = ndxWord.ParentNode;
          // Words that are directly attached to the 'top' get a special treatment
          if (ndxParent.Attributes["cat"].Value == "top") {
            // Those with a "top" parent get a special treatment
            // They will be treated further down
            oXmlTools.AddXmlAttribute(pdxCurrentFile, ref ndxListItem, "later", "true");
          } else {
            // Nodes initially get attached to the <forest> element
            ndxEtree = ndxListItem;
            XmlNode ndxListCopy = ndxFor.SelectSingleNode("./child::psdx:eTree[@Id=" + 
              ndxListItem.Attributes["Id"].Value + "]", nmsPsdx);
            // Walk upwards, but keep in <node> structure
            while (ndxParent != null && (ndxParent.Name == "node") && 
              (ndxParent.Attributes["done"].Value == "no") && 
              (ndxParent.Attributes["cat"].Value != "top")) {
              // This node has not yet been processed
              // (1) Determine label
              strLabel = ndxParent.Attributes["cat"].Value;
              if (!(util.General.DoLike(ndxParent.Attributes["rel"].Value, "hd|--")))
                  strLabel += "-" + ndxParent.Attributes["rel"].Value;
              // (2) Insert a node (and [ndxEtree] becomes this *new* node!
              if (!(oPsdxTools.eTreeInsertLevel(ref ndxListCopy, ref ndxEtree))) return false;
              loc_intEtreeId++;
              // (3) set the label of this new node
              ndxEtree.Attributes["Label"].Value = strLabel.ToUpper();
              ndxEtree.Attributes["Id"].Value = loc_intEtreeId.ToString();
              // (4) TODO: copy attributes to this node
              for (intI = 0; intI < ndxParent.Attributes.Count; intI++) {
                string strAttrName = ndxParent.Attributes[intI].Name;
                string strAttrValue = ndxParent.Attributes[intI].Value;
                if (strAttrName != "cat") {
                  oPsdxTools.AddFeature(ref pdxCurrentFile, ref ndxEtree, "alp", strAttrName, strAttrValue);
                }
              }
              // (x) Indicate in the Alpino tree that this node has been processed
              ndxParent.Attributes["done"].Value = "yes";
              // Go one up
              ndxParent = ndxParent.ParentNode;
              ndxListCopy = ndxListCopy.ParentNode;
            }
            // Check situation: is parent a "done" one?
            if (ndxParent != null && (ndxParent.Name == "node") && (ndxParent.Attributes["done"].Value == "yes")) {
              // ==========================================================
              // We need to append [ndxEtree] as child under the PSDX copy of the ALPINO node
              // =====================================================
              // (1) Find the PSDX copy
              XmlNode ndxNewParent = ndxFor.SelectSingleNode("./descendant::psdx:eTree[child::psdx:fs/child::psdx:f[@name='id' and @value='" + 
                ndxParent.Attributes["id"].Value + "']]", nmsPsdx);
              // (2) Double checking
              if (ndxNewParent == null) {
                errHandle.DoError("modConvert/OneAlpinoToPsdxForest", "warning: cannot find PSDX equivalent of ALPINO node");
                System.Diagnostics.Debugger.Break();
              }
              // (3) Check for well-formedness: 
              //     - Linear precedence: the last <eLeaf> child under my prospective PSDX parent
              //       must be my immediately linearly preceding neighbour
              //       Means: @end of rightmost ndxNewParent descendant == @begin of leftmost ndxEtree one
              //     - The word preceding me may not come after my parent-to-be in PSDX
              int intNprec = Convert.ToInt32(ndxEtree.SelectSingleNode("./descendant::psdx:eLeaf[1]", nmsPsdx).Attributes["n"].Value);
              // Dim ndxTest As XmlNode = ndxNewParent.SelectSingleNode("./following::eLeaf[@n=" & intNprec-1 & "]")
              XmlNode ndxTest = ndxNewParent.SelectSingleNode("./following::psdx:eLeaf[@n<" + intNprec + " and parent::psdx:eTree[not(@later)]]", nmsPsdx);
              if (ndxTest != null) {
                // (4) The order would be corrupted
                // (4.1) Create a new node under the 'ndxNewParent' we determined
                XmlNode ndxIchNode = null;
                XmlNode ndxIchLeaf = null;
                intIchCounter += 1;
                oPsdxTools.eTreeAdd(ref ndxNewParent, ref ndxIchNode, "child");
                // (4.2) This node gets the same @Label as I have, but a *ICH*-n child
                ndxIchNode.Attributes["Label"].Value = ndxEtree.Attributes["Label"].Value;
                oPsdxTools.eTreeAdd(ref ndxIchNode, ref ndxIchLeaf, "leaf");
                ndxIchLeaf.Attributes["Type"].Value = "Star";
                ndxIchLeaf.Attributes["Text"].Value = "*ICH*-" + intIchCounter;
                // (4.3) The [ndxEtree] get a "-n" attached to my label
                ndxEtree.Attributes["Label"].Value = ndxEtree.Attributes["Label"].Value + "-" + intIchCounter;
                // (4.4) The [ndxEtree] must append as child under the parent of the node in PSDX
                //         that has the @n minus 1
                int intN = Convert.ToInt32(ndxEtree.SelectSingleNode("./descendant::psdx:eLeaf[1]", nmsPsdx).Attributes["n"].Value);
                // (4.5) So find our new parent-to-be
                int intSubtract = 0;
                do {
                  intSubtract -= 1;
                  ndxNewParent = ndxFor.SelectSingleNode("./descendant::psdx:eTree[child::psdx:eLeaf[@n = " + 
                    (intN + intSubtract) + "]]", nmsPsdx);
                  // Double checking
                  if (ndxNewParent == null) {
                    errHandle.DoError("modConvert/OneAlpinoToPsdxForest", 
                      "warning: could not find leaf with @n=" + Convert.ToString(intN - 1));
                    System.Diagnostics.Debugger.Break();
                  }
                } while (!(ndxNewParent.ParentNode.Name != "forest"));
                // (4.6) we need to go one step upwards and check it still is an <eTree>
                ndxNewParent = ndxNewParent.ParentNode;
                if (ndxNewParent == null || ndxNewParent.Name != "eTree") {
                  errHandle.DoError("modConvert/OneAlpinoToPsdxForest", 
                    "warning: no suitable <eTree> grandparent for leaf with @n=" + Convert.ToString(intN - 1));
                  System.Diagnostics.Debugger.Break();
                }
              }
              // (5) The order is okay, so append [ndxEtree] as child
              ndxNewParent.AppendChild(ndxEtree);
            }
          }
        }

    
        // Find all the end-nodes with an index in ALPINO
        // ndxList = pdxSrc.SelectNodes("./descendant::alpino:node[@index > 0 and count(child::alpino:node) = 0 and not(@cat) and not(@lcat)]", nmsAlp);
        ndxFixed = oXmlTools.FixList(pdxSrc.SelectNodes( 
          "./descendant::alpino:node[@index > 0 and count(child::alpino:node) = 0 and not(@cat) and not(@lcat)]", 
          nmsAlp));

        // foreach (XmlNode ndxWordWithinLoop in ndxList) {
        foreach (XmlNode ndxWordWithinLoop in ndxFixed) {
          ndxWord = ndxWordWithinLoop;
          // Get my index value
          int intIndex = Convert.ToInt32(ndxWord.Attributes["index"].Value);
          // Determine the referential counter
          int intRefIndex = intIchCounter + intIndex;
          // Get the node in PSDX to which I should point
          XmlNode ndxIndexTarget = ndxFor.SelectSingleNode("./descendant::psdx:eTree[child::psdx:fs/child::psdx:f[@name='index' and @value='" + 
            intIndex + "']]", nmsPsdx);
          if (ndxIndexTarget == null) {
            errHandle.DoError("modConvert/OneAlpinoToPsdxForest", 
              "warning: could not find target for index=" + intIndex + 
              " at node id=[" + ndxWord.Attributes["id"].Value + "]");
            System.Diagnostics.Debugger.Break();
          } else {
            XmlNode ndxIchNode = null;
            XmlNode ndxIchLeaf = null;
            // Adapt the label of this constituent
            ndxIndexTarget.Attributes["Label"].Value += "-" + intRefIndex;
            // Determine my position by getting my parent, preceding sibling and following sibling
            int intParIdx = -1;
            int intBefIdx = -1;
            int intAftIdx = -1;
            XmlNode ndxPar = ndxWord.ParentNode;
            if (ndxPar != null)
                intParIdx = Convert.ToInt32(ndxPar.Attributes["id"].Value);
            // Validate
            if (ndxPar == null) {
              errHandle.DoError("modConvert/OneAlpinoToPsdxForest", 
                "warning: could not find parent of index=" + intIndex);
              System.Diagnostics.Debugger.Break();
            } else {
              // Find the equivalent parent within Psdx
              ndxPar = ndxFor.SelectSingleNode("./descendant::psdx:eTree[child::psdx:fs/child::psdx:f[@name='id' and @value='" + intParIdx + "']]", nmsPsdx);
              // Find the preceding sibling and following sibling within ALP
              XmlNode ndxBef = ndxWord.PreviousSibling;
              if (ndxBef != null)
                  intBefIdx = Convert.ToInt32(ndxBef.Attributes["id"].Value);
              XmlNode ndxAft = ndxWord.NextSibling;
              if (ndxAft != null)
                  intAftIdx = Convert.ToInt32(ndxAft.Attributes["id"].Value);
              // Try find the corresponding nodes in PSDX
              if (intBefIdx >= 0)
                ndxBef = ndxFor.SelectSingleNode("./descendant::psdx:eTree[child::psdx:fs/child::psdx:f[@name='id' and @value='" + intBefIdx + "']]", nmsPsdx);
              if (intAftIdx >= 0)
                ndxAft = ndxFor.SelectSingleNode("./descendant::psdx:eTree[child::psdx:fs/child::psdx:f[@name='id' and @value='" + intAftIdx + "']]", nmsPsdx);
              // Find out where a node should be created in the target PSDX
              if (ndxBef == null) {
                if (ndxAft == null){
                  // No before and no after --> only child
                  oPsdxTools.eTreeAdd(ref ndxPar, ref ndxIchNode, "child");
                } else {
                  // Insert a new child before ndxAft
                  oPsdxTools.eTreeAdd(ref ndxAft, ref ndxIchNode, "left");
                }
              } else {
                  // Insert a new child after ndxBef
                oPsdxTools.eTreeAdd(ref ndxBef, ref ndxIchNode, "right");
              }
              // Double check
              if (ndxIchNode != null)
              {
                // Adapt the label of the new node
                string sLabel = ndxIndexTarget.Attributes["Label"].Value;
                int intIdx = sLabel.IndexOf("-") + 1;
                if (intIdx > 0)
                {
                    sLabel = sLabel.Substring(0, intIdx - 1);
                }
                ndxIchNode.Attributes["Label"].Value = sLabel + "-" + ndxWord.Attributes["rel"].Value.ToUpper();
                // Copy the attributes from source to target
                for (intI = 0; intI < ndxWord.Attributes.Count; intI++)
                {
                  string strAttrName = ndxWord.Attributes[intI].Name;
                  string strAttrValue = ndxWord.Attributes[intI].Value;
                  if (strAttrName != "cat")
                     oPsdxTools.AddFeature(ref pdxCurrentFile, ref ndxIchNode, "alp", strAttrName, strAttrValue);
                }
                // Add an eLeaf node to the ICH
                oPsdxTools.eTreeAdd(ref ndxIchNode, ref ndxIchLeaf, "leaf");
                ndxIchLeaf.Attributes["Type"].Value = "Star";
                ndxIchLeaf.Attributes["Text"].Value = "*T*-" + intRefIndex;
              }
            }
          }
        }

        // Now do the words marked 'later'
        // ndxList = ndxFor.SelectNodes("./child::psdx:eTree[@later]", nmsPsdx);
        ndxFixed = oXmlTools.FixList(ndxFor.SelectNodes("./child::psdx:eTree[@later]", nmsPsdx));
        foreach (XmlNode ndxThisWithinLoop in ndxFixed) {
          ndxThis = ndxThisWithinLoop;
          // Find this node in the Alpino source [pdxSrc]
          ndxWord = pdxSrc.SelectSingleNode("./descendant::alpino:node[@id='" +
            oPsdxTools.GetFeature(ndxThis, "alp", "id") + "']", nmsAlp);
          // Find the words between which it has to be plugged
          int intN = Convert.ToInt32(ndxThisWithinLoop.SelectSingleNode("./child::psdx:eLeaf", nmsPsdx).Attributes["n"].Value);
          XmlNode ndxBef = ndxFor.SelectSingleNode("./descendant::psdx:eTree[child::psdx:eLeaf[@n=" + (intN - 1) + "]]", nmsPsdx);
          XmlNode ndxAft = ndxFor.SelectSingleNode("./descendant::psdx:eTree[child::psdx:eLeaf[@n=" + (intN + 1) + "]]", nmsPsdx);
          // Check which situation we are in
          if (ndxBef == null || ndxBef.Name == "forest") {
            // Must plug in before [ndxAft]
            // (1) Get first eTree child under forest
            var ndxFirst = ndxFor.SelectSingleNode("./child::psdx:eTree[not(@later)][1]", nmsPsdx);
            // Validate there are any <eTree> children that do not have "later"
            if (ndxFirst == null) {
              // There are no <eTree> children of <forest> that are NOT marked @later, so no displacements are called for
              // errHandle.DoError("AlpinoToPsdx/OneAlpinoToPsdxForest", "could not find <eTree> without @later");
              int iDebug = 0;
            } else {
              // (2) Prepend before this one
              ndxFirst.PrependChild(ndxThisWithinLoop);
            }
          } else if (ndxAft == null || ndxAft.Name == "forest") {
            // Must plug in after [ndxBef]
            // (1) Get last eTree child under forest
            var ndxLast = ndxFor.SelectSingleNode("./child::psdx:eTree[not(@later)][last()]", nmsPsdx);
            // (2) Append after this one
            ndxLast.AppendChild(ndxThisWithinLoop);
          } else {
            // Must plug in between [Bef - Aft]
            string strCond = ""; // "[child::fs/child::f[@name = 'cat']/@value != 'top']"
            XmlNode ndxLeft = null;
            XmlNode ndxRight = null;
            XmlNode ndxCommon = oXmlTools.getCommonAncestor(ref ndxBef, ref ndxAft, strCond, ref ndxLeft, ref ndxRight);
            if (ndxCommon == null)
            {
              // If this happens, we will take the parent of [ndxBef] as the one after we need to plug in
              ndxBef.ParentNode.InsertAfter(ndxThisWithinLoop, ndxBef);
            } else {
              // Add the node to this ancestor, but after [ndxBef]
              ndxCommon.InsertAfter(ndxThisWithinLoop, ndxLeft);
            }
          }
          // Remove the @later
          ndxThisWithinLoop.Attributes.Remove(ndxThisWithinLoop.Attributes["later"]);
        }


        // (5) Make sure the sentence is re-analyzed
        ndxThis = null;
        oPsdxTools.eTreeSentence(ref ndxFor, ref ndxThis);
        // Return success
        return true;
      }
      catch (Exception ex)
      {
        // Give error
        errHandle.DoError("modConvert/OneAlpinoToPsdxForest", ex);
        // Return failre
        return false;
      }
    }
    static void debug(String sMsg) { Console.WriteLine(sMsg); }

  }
}

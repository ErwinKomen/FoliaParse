using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace FoliaParse.conv {
  /* -------------------------------------------------------------------------------------
   * Name:  PsdxToFolia
   * Goal:  The "XmlConv" implementation to convert the psdx <forest>, <eTree>, <eLeaf> structure
   *        to the FoLiA one with <s>, <syntax>, <su>, <w> etc
   * History:
   * 2/oct/2015 ERK Created
     ------------------------------------------------------------------------------------- */
  public class PsdxToFolia : XmlConv {
    // ============== Local constants ==========================================
    private String strDefault = "<?xml version='1.0' encoding='utf-8'?>\n" +
      "<?xml-stylesheet type='text/xsl' href='foliaviewer.xsl'?>\n" +
      "<FoLiA xmlns:xlink='http://www.w3.org/1999/xlink' xmlns='http://ilk.uvt.nl/folia' xml:id='' version='0.0.1' generator='Cesax'>\n" +
      "<metadata type='native'>\n<annotations>\n" +
      " <token-annotation annotator='FoLiA+Sonar' annotatortype='auto'/>\n" +
      " <pos-annotation annotator='FoLiA+Sonar' annotatortype='auto'/>\n" +
      " <syntax-annotation annotator='Alpino+Cesax' annotatortype='auto'/>\n" +
      "</annotations>\n</metadata>\n" +
      "<text xml:id='fill_this_in.text'>\n" +
      "<s xml:id='dummy.s.1'></s>\n" +
      "</text></FoLiA>\n";
    private bool bWinsert = true;
    // ============ Local variables ============================================
    XmlDocument pdxFolia;
    // ============ Class initializer calls the base class =====================
    public PsdxToFolia(ErrHandle objErr) { 
      this.errHandle = objErr; 
      this.oXmlTools = new util.xmlTools(objErr);
      this.oPsdxTools = new util.psdxTools(objErr, null);
    }

    // ===================== getters and setters ==========================================
    public override String getCurrentSrcFile() { return this.sCurrentSrcFile; }
    public override void setCurrentSrcFile(String sFile) { this.sCurrentSrcFile = sFile; }


    /* -------------------------------------------------------------------------------------
     * Name:  oneSent
     * Goal:  Convert one sentence from psdx to folia
     * History:
     * 2/oct/2015   ERK Created
     * 28/nov/2015  ERK Added [lWords] argument
       ------------------------------------------------------------------------------------- */
    public override XmlNode oneSent(XmlNode ndxPsdx, String sSentId, String sArg, ref List<XmlNode> lWords) {
      XmlDocument pdxThis = new XmlDocument();
      XmlNode ndxBack = null;

      try {
        // Psdx conversion specific: need to create a bare <FoLiA> structure
        pdxFolia = new XmlDocument();
        pdxFolia.LoadXml(strDefault);
        // Set up a namespace manager for folia
        XmlNamespaceManager nmsDf = new XmlNamespaceManager(pdxFolia.NameTable);
        nmsDf.AddNamespace("df", pdxFolia.DocumentElement.NamespaceURI);

        // Make sure the xmltools have the correct target
        oXmlTools.SetXmlDocument(pdxFolia);

        // And the psdxtools need to point to the source
        oPsdxTools.setCurrentFile(ndxPsdx.OwnerDocument);

        // Convert from psdx to folia
        XmlNode ndxDummyFoliaS = pdxFolia.SelectSingleNode("./descendant::df:s", nmsDf);
        if (!OnePsdxToFoliaForest(ref ndxPsdx, ref ndxDummyFoliaS, sSentId, nmsDf, ref lWords)) {
          errHandle.DoError("PsdxToFolia/oneSent", "Failed to complete OnePsdxToFoliaForest");
          return null;
        }

        // Return the result: the <s> part of the transformation (skip the remainder)
        ndxBack = ndxDummyFoliaS;
        return ndxBack;
      } catch (Exception ex) {
        errHandle.DoError("oneSent", ex); // Provide standard error message
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
    // Name:       OnePsdxToFoliaForest()
    // Goal:       Process one psdx <forest> element into folia format
    //             The <w> nodes must be placed under the <s> (=[ndxS]) node
    //             The structure must be placed as <su> as child under <s>
    //             The ID of this sentence is passed on in [strSid]
    // Parameters: ndxFor  - the <forest> sentence in Psdx
    //             ndxS    - the <s> structure produced for FoLiA
    //             strSid  - Folia xml:id of the current sentence
    //             nsDF    - namespace manager for FoLiA, defining the "df:" prefix
    //             lWords  - list of <w> elements. The list may be modified and expanded
    // History:
    // 01-03-2014  ERK Created in VB.NET
    // 05-10-2015  ERK Ported to C#
    // 28/nov/2015 ERK Added [lWords] argument
    //----------------------------------------------------------------------------------------
    private bool OnePsdxToFoliaForest(ref XmlNode ndxFor, ref XmlNode ndxS, string strSid, XmlNamespaceManager nsDF, ref List<XmlNode> lWords) {
      XmlNodeList ndxList = null; // List of nodes
      XmlNodeList ndxFeat = null; // List of features
      XmlNode ndxLeaf = null;     // The leaf
      string strLemma = null;     // Lemma
      string strTclass = null;    // The class for the <t> element in a sentence
      XmlNode ndxW = null;        // One <w> node
      XmlNode ndxT = null;        // One <t> node
      XmlNode ndxF = null;        // One <feat> node
      XmlNode ndxL = null;        // One <lemma> node
      XmlNode ndxPos = null;      // One <pos> node
      XmlNode ndxSyntax = null;   // One <syntax> node
      string strWid = null;       // ID of this word
      int intI = 0;               // Counter
      int intJ = 0;               // Counter

      try {
        // Validate
        if ((ndxFor == null) || (ndxFor.Name != "forest")) {
          errHandle.Status("OnePsdxToFoliaForest: expect <forest> input");
          return false;
        }
        if ((ndxS == null) || (ndxS.Name != "s")) {
          errHandle.Status("OnePsdxToFoliaForest: <s> expected");
          return false;
        }
        if (string.IsNullOrEmpty(strSid)) return false;
        if (nsDF == null) return false;
        // Get the text of this sentence in all applicable languages
        ndxList = ndxFor.SelectNodes("./child::div");
        for (intI = 0; intI < ndxList.Count; intI++) {
          // Determine the class for this element
          strTclass = ndxList[intI].Attributes["lang"].Value;
          if (strTclass == "org")  strTclass = "original";
          // Create the <t> element under the <s>
          ndxT = oXmlTools.AddXmlChild(ndxS, "t", "class", strTclass, "attribute");
          ndxT.InnerText = ndxList[intI].SelectSingleNode("./child::seg").InnerText;
        }
        // Get a list of <eTree> nodes that have an <eLeaf> child
        ndxList = ndxFor.SelectNodes("./descendant::eTree[count(child::eLeaf)>0]");
        // int iWordCount = 0; // Separate counter to keep track of corresponding ones in [lWords]
        // int iStarId = 1;    // ID's for star and zero's
        for (intI = 0; intI < ndxList.Count; intI++) {
          // Capture the current node
          XmlNode ndxCurrentNode = ndxList[intI];
          // Get to the leaf
          ndxLeaf = ndxCurrentNode.SelectSingleNode("./child::eLeaf[1]");
          //// Should we increment the word counter?
          //switch (ndxLeaf.Attributes["Type"].Value) {
          //  case "Star":
          //  case "Zero":
          //    // Create a unique id for this star/zero item
          //    strWid = strSid +".z." + iStarId;
          //    // Keep count of the star/zero items
          //    iStarId++;
          //    break;
          //  default:
          //    // Retrieve the original <w> id 
          //    strWid = lWords[iWordCount].Attributes["xml:id"].Value;
          //    // Keep track of the count in the [lWords] list
          //    iWordCount++;                                           
          //    break;
          //}
          ////if (intI < lWords.Count) {
          ////  strWid = lWords[intI].Attributes["xml:id"].Value;
          ////} else {
          ////  strWid = strSid + "." + (intI + 1);
          ////}
          strWid = strSid + "." + (intI + 1);

          // Create a new <w> node under <s>
          ndxW = oXmlTools.AddXmlChild(ndxS, "w", "xml:id", strWid, "attribute", 
            "class", ndxLeaf.Attributes["Type"].Value, "attribute", 
            "t", ndxLeaf.Attributes["Text"].Value, "child");
          // Try to find a lemma feature
          strLemma = oPsdxTools.GetFeature(ndxCurrentNode, "M", "l");
          if (!string.IsNullOrEmpty(strLemma)) {
            // Add a lemma child to <w>
              ndxL = oXmlTools.AddXmlChild(ndxW, "lemma", "class", strLemma, "attribute");
          }
          // Add the POS tag
          ndxPos = oXmlTools.AddXmlChild(ndxW, "pos", "class", ndxCurrentNode.Attributes["Label"].Value, "attribute");
          // Find any other features
          ndxFeat = ndxCurrentNode.SelectNodes("./child::fs/child::f[not(@name='l' and parent::fs/@type='M')]");
          for (intJ = 0; intJ < ndxFeat.Count; intJ++) {
            // Add this feature
              ndxF = oXmlTools.AddXmlChild(ndxPos, "feat", 
                "subset", ndxFeat[intJ].ParentNode.Attributes["type"].Value + "/" + ndxFeat[intJ].Attributes["name"].Value, "attribute", 
                "class", ndxFeat[intJ].Attributes["value"].Value, "attribute");
          }
        }
        // Create a <syntax> node under the sentence
        ndxSyntax = oXmlTools.AddXmlChild(ndxS, "syntax");
        // Add all children under <forest>
        ndxList = ndxFor.SelectNodes("./child::eTree");
        for (intI = 0; intI < ndxList.Count; intI++) {
          // Capture the current node
          XmlNode ndxCurrentNode = ndxList[intI];
          // Add this child and whatever is under it recursively
          if (!OnePsdxToFoliaSu(ref ndxSyntax, ref ndxCurrentNode, strSid, nsDF, ref lWords)) {
            return false;
          }
        }
        // Return success
        return true;
      } catch (Exception ex) {
        // Give error
        errHandle.DoError("modConvert/OnePsdxToFoliaForest", ex);
        // Return failre
        return false;
      }
    }
    //----------------------------------------------------------------------------------------
    // Name:       OnePsdxToFoliaSu()
    // Goal:       Add psdx element [ndxThis] as <su> under [ndxParentFolia]
    // History:
    // 12-03-2014  ERK Created
    // 28/nov/2015 ERK Added [lWords] argument
    //----------------------------------------------------------------------------------------
    private bool OnePsdxToFoliaSu(ref XmlNode ndxParentFolia, ref XmlNode ndxThis, string strSid, 
        XmlNamespaceManager nsDF, ref List<XmlNode> lWords) {
      XmlNodeList ndxList = null; // List of nodes
      XmlNodeList ndxFeat = null; // List of features
      XmlNode ndxLeaf = null;     // An <eLeaf> node
      XmlNode ndxF = null;        // One <feat> node
      XmlNode ndxSu = null;       // One <su> node
      XmlNode ndxWref = null;     // One <wref> element
      string strWid = null;       // the ID of the word
      string strSuId = null;      // ID of this syntactic unit
      string strWinsert = "";     // How to refer to a word
      int intI = 0; // Counter
      int intJ = 0; // Counter
      bool bUseNs = false;

      try {
        // Validate
        if ((ndxParentFolia == null) || (ndxThis == null)) {
          return false;
        }
        // Initialisation: determine what to insert for <w> reference
        strWinsert = (bWinsert) ? ".w." : ".";
        // Determine the xml:id of this syntactic unit
        // strSuId = strSid + ".su." + (ndxParentFolia.SelectNodes("./ancestor-or-self::df:syntax/descendant::df:su", nsDF).Count + 1);
        if (bUseNs) {
          strSuId = strSid + ".su." + (ndxParentFolia.SelectNodes("./ancestor-or-self::df:syntax/descendant::df:su", nsDF).Count + 1);
        } else {
          strSuId = strSid + ".su." + (ndxParentFolia.SelectNodes("./ancestor-or-self::syntax/descendant::su").Count + 1);
        }
        // XmlNodeList ndList = ndxParentFolia.SelectNodes("./ancestor-or-self::df:syntax", nsDF);
        // XmlNode ndTarget = ndxParentFolia.SelectSingleNode("./ancestor-or-self::df:syntax", nsDF);

        // Add the [ndxThis] contents
        ndxSu = oXmlTools.AddXmlChild(ndxParentFolia, "su", "xml:id", strSuId, "attribute", 
          "class", ndxThis.Attributes["Label"].Value, "attribute");
        // Add features
        ndxFeat = ndxThis.SelectNodes("./child::fs/child::f");
        for (intJ = 0; intJ < ndxFeat.Count; intJ++) {
          // Add this feature
          ndxF = oXmlTools.AddXmlChild(ndxSu, "feat", 
            "subset", ndxFeat[intJ].ParentNode.Attributes["type"].Value + "/" + ndxFeat[intJ].Attributes["name"].Value, "attribute", 
            "class", ndxFeat[intJ].Attributes["value"].Value, "attribute");
        }
        // Check out if this is an endnode
        ndxLeaf = ndxThis.SelectSingleNode("./child::eLeaf[1]");
        if (ndxLeaf != null) {
          XmlNode ndxW;
          // Action depends on the type of leaf
          switch (ndxLeaf.Attributes["Type"].Value) {
            case "Zero": case "Star":
              // Create a new <w> element
              strWid = strSid + strWinsert + (lWords.Count + 1);
              XmlNode ndxS;
              if (bUseNs) {
                ndxS = lWords[0].SelectSingleNode("./ancestor-or-self::df:s", nsDF);
              } else {
                ndxS = lWords[0].SelectSingleNode("./ancestor-or-self::s");
                if (ndxS == null)
                  ndxS = lWords[0].SelectSingleNode("./ancestor-or-self::df:s", nsDF);
              }
              oXmlTools.SetXmlDocument(ndxS.OwnerDocument);
              ndxW = oXmlTools.AddXmlChild(ndxS, "w", 
                "xml:id", strWid, "attribute",
                "class", ndxLeaf.Attributes["Type"].Value, "attribute",
                "t", ndxLeaf.Attributes["Text"].Value, "child");
              // Add a POS child
              XmlNode ndxPos = oXmlTools.AddXmlChild(ndxW, "pos",
                "class", ndxSu.Attributes["class"].Value, "attribute");
              // Add this entry to the list
              lWords.Add(ndxW);
              break;
            default:
              // Find out the number of this word/punct (but excluse Zero and Star)
              int iWordBef = ndxThis.SelectNodes(
                "./ancestor::forest/descendant::eLeaf[@to < " +
                ndxLeaf.Attributes["from"].Value + " and @Type!= 'Zero' and @Type !='Star']").Count;
              // Calculate the Wid: should contain .w. if that is included in the original
              // strWid = strSid + strWinsert + (ndxThis.SelectNodes("./ancestor::forest/descendant::eLeaf[@to < " + ndxLeaf.Attributes["from"].Value + "]").Count + 1);
              strWid = strSid + strWinsert + (iWordBef + 1);
              Boolean bFound = false;
              // Find the corresponding <w> entry in the list
              for (int j = 0; j < lWords.Count; j++) {
                if (lWords[j].Attributes["xml:id"].Value == strWid) {
                  // Add the @class attribute
                  oXmlTools.SetXmlDocument(lWords[j].OwnerDocument);
                  oXmlTools.AddAttribute(lWords[j], "class", ndxLeaf.Attributes["Type"].Value);
                  bFound = true;  
                  // Get out of the for-loop
                  break;
                }
              }
              // Validation
              if (!bFound) {
                int iStop = 1;
              }
              break;
          }
          // Add the <wref> element pointing to the <eLeaf> within Folia
          oXmlTools.SetXmlDocument(ndxSu.OwnerDocument);
          ndxWref = oXmlTools.AddXmlChild(ndxSu, "wref", 
            "id", strWid, "attribute", 
            "t", ndxLeaf.Attributes["Text"].Value, "attribute");
        }
        // Process all my psdx children
        ndxList = ndxThis.SelectNodes("./child::eTree");
        for (intI = 0; intI < ndxList.Count; intI++) {
          // Capture the current node
          XmlNode ndxCurrentNode = ndxList[intI];
          // Process this child
          if (!(OnePsdxToFoliaSu(ref ndxSu, ref ndxCurrentNode, strSid, nsDF, ref lWords))) {
            return false;
          }
        }
        // Return success
        return true;
      } catch (Exception ex) {
        // Give error
        errHandle.DoError("modConvert/OnePsdxToFoliaSu", ex);
        // Return failre
        return false;
      }
    }


  }
}

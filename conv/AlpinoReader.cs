using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using FoliaParse.conv;
using System.Diagnostics;

namespace FoliaParse.conv {
  class AlpinoReader {
    // ========================= Declarations local to me =================================
    private ErrHandle errHandle = new ErrHandle();
    private AlpIndex lstAlpIndex = null;
    private String loc_sDirParsed;        // Directory containing .data.dz and .index files of Alpino
    private String loc_sDirTemp;          // Temporary directory to be used
    private String[] loc_arParsed;        // Sorted array of .index files inside loc_sDirParsed
    private int loc_iParsed;              // Current index into loc_arParsed
    private int loc_iAlpIndex;            // Index into the current [lstAlpIndex] list
    private bool loc_bReady = false;      // Ready to take the first 'next' request or not?
    private FileStream loc_fFileAlpino;   // Currently opened alpino .data file
    // ========================= Class initialiser ========================================
    public AlpinoReader(String sDirParsed, String sDirTemp) {
      this.loc_sDirParsed = sDirParsed;
      // Initialisations
      this.loc_arParsed = null;
      this.loc_iParsed = -1;
      this.loc_fFileAlpino = null;
      // Identify and possibly create a temporary directory
      this.loc_sDirTemp = sDirTemp + "/tmp";
      if (!Directory.Exists(loc_sDirTemp)) Directory.CreateDirectory(loc_sDirTemp);
      // Are there any parsed files?
      if (sDirParsed != "" && Directory.Exists(sDirParsed)) {
        // Get an array of .index files pointing to the parsed files
        loc_arParsed = util.General.getFilesSorted(sDirParsed, "*.index", "name").ToArray();
        if (loc_arParsed.Length > 0) {
          // Read the first index file
          loc_iParsed = 0;
          this.lstAlpIndex = new AlpIndex(loc_arParsed[loc_iParsed]);
          // Unpack and open the corresponding .data file
          if (unpackAndOpenDataFile(loc_arParsed[loc_iParsed]))
            loc_iAlpIndex = 0;  // Signal that we are ready
          else
            loc_iAlpIndex = -1; // Signal there is failure
        } else {
          errHandle.DoError("AlpinoReader/classInit", "Cannot find *.index files in dir [" + sDirParsed + "]");
        }
      }
    }
    // ========================== GETters =================================================
    public String getCurrentSentId() { return lstAlpIndex.index[loc_iAlpIndex].sentId; }
    public FileStream getCurrentFile() { return this.loc_fFileAlpino; }
    public AlpIndexEl getCurrentAlpIndexEl() { return this.lstAlpIndex.index[this.loc_iAlpIndex]; }
    public bool ready() { return this.loc_bReady; }
    // ====================================================================================

    /// <summary>
    /// Starting from the very first index file, look for the first occurrence of [sSentId]
    /// Then position the reader at the start of this entry
    /// </summary>
    /// <param name="sSentId">The string id of the sentence being looked for</param>
    /// <returns>true upon success</returns>
    public bool findSentId(String sSentId) {
      try {
        errHandle.Status("findSentId: " + sSentId + "...");
        // Initialisations:
        loc_iParsed = 0;
        loc_iAlpIndex = -1;
        // Sanity check
        if (loc_arParsed == null) {
          errHandle.DoError("AlpinoReader/findSentId", "There is no [loc_arParsed]");
          return false;
        }
        // Walk all index files from beginning until end
        for (int i = 0; i < loc_arParsed.Length; i++) {
          // Show where we are
          errHandle.Status("findSentId: looking at " + loc_arParsed[i]);
          // Read this index file
          this.lstAlpIndex = new AlpIndex(loc_arParsed[i]);
          List<AlpIndexEl> lstThis = this.lstAlpIndex.index;
          // Traverse the index file
          for (int j = 0; j < lstThis.Count; j++) {
            // Check if this is the correct index
            if (lstThis[j].sentId == sSentId || lstThis[j].sentId == sSentId + ".xml") {
              // We found the correct sentence!!
              // Set the correct index pointer
              loc_iParsed = i;
              // Unpack the .data file
              if (unpackAndOpenDataFile(loc_arParsed[loc_iParsed])) {
                // Set the correct pointer within the index file
                loc_iAlpIndex = j;
                // Indicate that we are ready overall
                this.loc_bReady = true;
                // Tell the world
                errHandle.Status("findSentId: FOUND IT: " + sSentId);
                // Return success
                return true;
              } else {
                // Failure -- did not find anything
                errHandle.Status("findSentId: found " + sSentId + " but could not unpack .data file");
                return false;
              }
            }
          }
        }
        // Failure -- did not find anything
        errHandle.Status("findSentId: did not find " + sSentId);
        return false;
      } catch (Exception ex) {
        this.errHandle.DoError("AlpinoReader/findSentId", ex);
        return false;
      }
    }

    /// <summary>
    /// Given an index file, find the .data.dz file, decompress it and open it as read-only random-access
    /// </summary>
    /// <param name="sIndexFile"></param>
    /// <returns>true upon success</returns>
    private bool unpackAndOpenDataFile(String sIndexFile) {
      try {
        // Derive the data file name
        String sCompressedFile = sIndexFile.Replace(".index", ".data.dz");
        // Does the file exist?
        if (!File.Exists(sCompressedFile)) return false;
        // Make room for the temporarily uncompressed file
        String sFileParsed = loc_sDirTemp + "/temp.data";
        // Delete a possible previous file
        if (File.Exists(sFileParsed)) {
          // If the file handle still exists...
          if (this.loc_fFileAlpino != null) this.loc_fFileAlpino.Close();
          // Actually delete it
          File.Delete(sFileParsed);
        }
        // Decompress the file 
        util.General.DecompressFile(sCompressedFile, sFileParsed);
        // Set a handle to this file
        this.loc_fFileAlpino = new FileStream(sFileParsed, FileMode.Open, FileAccess.Read, FileShare.Read);
        // Return success
        return true;
      } catch (Exception ex) {
        this.errHandle.DoError("AlpinoReader/unpackAndOpenDataFile", ex);
        return false;
      }
    }

    /// <summary>
    /// Given: 
    ///   a) the directory @loc_sDirParsed, containing .data.dz and .index files from Alpino,
    ///   b) the sentence identifier @sSentId (e.g. from FoLiA)
    /// Tasks:
    ///   1) find the correct Alpino file, 
    ///   2) unpack the .data.dz into .data, 
    ///   3) provide a pointer @fFileAlpino to it, 
    ///   4) return the index element @elIndex to the correct place where the sentence parse is located.
    /// Note: this makes use of class-variables:
    ///   lstAlpIndex  - contains the latest index list
    /// </summary>
    /// <param name="sDirParsed"></param>
    /// <param name="sSentId"></param>
    /// <param name="fFileAlpino"></param>
    /// <param name="elIndex"></param>
    /// <returns></returns>
    public bool nextAlpinoIndex(String sSentId, ref FileStream fFileAlpino, ref AlpIndexEl elIndex) {
      try {
        // Validate
        if (loc_arParsed == null || loc_iParsed<0) return false;
        // Check if we need to advance the pointer
        if (loc_iAlpIndex >= this.lstAlpIndex.index.Count) {
          // Should read a new index
          loc_iParsed++;
          // Validate
          if (loc_iParsed >= loc_arParsed.Length) return false;
          // Read the new index
          this.lstAlpIndex = new AlpIndex(loc_arParsed[loc_iParsed]);
          // Unpack and open the corresponding .data file
          if (unpackAndOpenDataFile(loc_arParsed[loc_iParsed]))
            loc_iAlpIndex = 0;  // Signal that we are ready
          else {
            loc_iAlpIndex = -1; // Signal there is failure
            return false;
          }
        }
        // Expecting [sSentId] to be right here, so look for it
        AlpIndexEl elThis = this.lstAlpIndex.index[loc_iAlpIndex];
        if (elThis.sentId == sSentId || elThis.sentId == sSentId + ".xml") {
          // Okay we have the correct index: 
          elIndex = elThis;
          // Go to the next position within this index
          loc_iAlpIndex++;
          // Make sure we return the most up-to-date file handle
          fFileAlpino = this.loc_fFileAlpino;
          // Return success
          return true;
        } else {
          // We do not have the correct index here
          errHandle.Status("nextAlpinoIndex: looking for [" + sSentId + "] finding [" + elThis.sentId + "]");
          // Indicate this by failure, but *do NOT advance the reading position*
          return false;
        }
      } catch (Exception ex) {
        this.errHandle.DoError("AlpinoReader/nextAlpinoIndex", ex);
        return false;
      }
    }

  }

  // ======================== OTHER CLASSES ================================================================

  /// <summary>
  /// Reader and container of an Alpino index
  /// </summary>
  class AlpIndex {
    private String[] arIndexAlpino;
    public List<AlpIndexEl> index = new List<AlpIndexEl>();
    public AlpIndex(String sFileIndex) {
      // Read index into array
      arIndexAlpino = File.ReadAllLines(sFileIndex);
      // Create list
      for (int i = 0; i < arIndexAlpino.Length; i++) {
        String sLine = arIndexAlpino[i];
        if (sLine != "") {
          this.index.Add(new AlpIndexEl(sLine));
        }
      }
    }
  }
  /// <summary>
  /// One alpino index element: sentence id, start and size
  /// </summary>
  class AlpIndexEl {
    public String sentId;
    public long start;
    public long size;
    // Create one line
    public AlpIndexEl(String sLine) {
      String[] arPart = sLine.Split('\t');
      if (arPart.Length == 3) {
        this.sentId = arPart[0];
        this.start = FoliaParse.util.General.base64ToInt(arPart[1]);
        this.size = FoliaParse.util.General.base64ToInt(arPart[2]);
      }
    }
  }
}

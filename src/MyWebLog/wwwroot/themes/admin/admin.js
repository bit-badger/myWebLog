const Admin = {
  /** The next index for a metadata item */
  nextMetaIndex : 0,

  /** The next index for a permalink */
  nextPermalink : 0,
  
  /**
   * Set the next meta item index
   * @param idx The index to set
   */
  setNextMetaIndex(idx) {
    this.nextMetaIndex = idx
  },

  /**
   * Set the next permalink index
   * @param idx The index to set
   */
  setPermalinkIndex(idx) {
    this.nextPermalink = idx
  },

  /**
   * Add a new row for metadata entry
   */
  addMetaItem() {
    // Remove button
    const removeBtn = document.createElement("button")
    removeBtn.type      = "button"
    removeBtn.className = "btn btn-sm btn-danger"
    removeBtn.innerHTML = "&minus;"
    removeBtn.setAttribute("onclick", `Admin.removeMetaItem(${this.nextMetaIndex})`)
    
    const removeCol = document.createElement("div")
    removeCol.className = "col-1 text-center align-self-center"
    removeCol.appendChild(removeBtn)
    
    // Name
    const nameField = document.createElement("input")
    nameField.type        = "text"
    nameField.name        = "metaNames"
    nameField.id          = `metaNames_${this.nextMetaIndex}`
    nameField.className   = "form-control"
    nameField.placeholder = "Name"
    
    const nameLabel = document.createElement("label")
    nameLabel.htmlFor   = nameField.id
    nameLabel.innerText = nameField.placeholder
    
    const nameFloat = document.createElement("div")
    nameFloat.className = "form-floating"
    nameFloat.appendChild(nameField)
    nameFloat.appendChild(nameLabel)
    
    const nameCol = document.createElement("div")
    nameCol.className = "col-3"
    nameCol.appendChild(nameFloat)

    // Value
    const valueField = document.createElement("input")
    valueField.type        = "text"
    valueField.name        = "metaValues"
    valueField.id          = `metaValues_${this.nextMetaIndex}`
    valueField.className   = "form-control"
    valueField.placeholder = "Value"

    const valueLabel = document.createElement("label")
    valueLabel.htmlFor   = valueField.id
    valueLabel.innerText = valueField.placeholder

    const valueFloat = document.createElement("div")
    valueFloat.className = "form-floating"
    valueFloat.appendChild(valueField)
    valueFloat.appendChild(valueLabel)

    const valueCol = document.createElement("div")
    valueCol.className = "col-8"
    valueCol.appendChild(valueFloat)
    
    // Put it all together
    const newRow = document.createElement("div")
    newRow.className = "row mb-3"
    newRow.id        = `meta_${this.nextMetaIndex}`
    newRow.appendChild(removeCol)
    newRow.appendChild(nameCol)
    newRow.appendChild(valueCol)
    
    document.getElementById("metaItems").appendChild(newRow)
    document.getElementById(nameField.id).focus()
    this.nextMetaIndex++
  },

  /**
   * Add a new row for a permalink
   */
  addPermalink() {
    // Remove button
    const removeBtn = document.createElement("button")
    removeBtn.type      = "button"
    removeBtn.className = "btn btn-sm btn-danger"
    removeBtn.innerHTML = "&minus;"
    removeBtn.setAttribute("onclick", `Admin.removePermalink(${this.nextPermalink})`)

    const removeCol = document.createElement("div")
    removeCol.className = "col-1 text-center align-self-center"
    removeCol.appendChild(removeBtn)

    // Link
    const linkField = document.createElement("input")
    linkField.type        = "text"
    linkField.name        = "prior"
    linkField.id          = `prior_${this.nextPermalink}`
    linkField.className   = "form-control"
    linkField.placeholder = "Link"

    const linkLabel = document.createElement("label")
    linkLabel.htmlFor   = linkField.id
    linkLabel.innerText = linkField.placeholder

    const linkFloat = document.createElement("div")
    linkFloat.className = "form-floating"
    linkFloat.appendChild(linkField)
    linkFloat.appendChild(linkLabel)

    const linkCol = document.createElement("div")
    linkCol.className = "col-11"
    linkCol.appendChild(linkFloat)

    // Put it all together
    const newRow = document.createElement("div")
    newRow.className = "row mb-3"
    newRow.id        = `meta_${this.nextPermalink}`
    newRow.appendChild(removeCol)
    newRow.appendChild(linkCol)

    document.getElementById("permalinks").appendChild(newRow)
    document.getElementById(linkField.id).focus()
    this.nextPermalink++
  },

  /**
   * Remove a metadata item
   * @param idx The index of the metadata item to remove
   */
  removeMetaItem(idx) {
    document.getElementById(`meta_${idx}`).remove()
  },

  /**
   * Remove a permalink
   * @param idx The index of the permalink to remove
   */
  removePermalink(idx) {
    document.getElementById(`link_${idx}`).remove()
  },

  /**
   * Confirm and delete a category
   * @param id The ID of the category to be deleted
   * @param name The name of the category to be deleted
   */
  deleteCategory(id, name) {
    if (confirm(`Are you sure you want to delete the category "${name}"? This action cannot be undone.`)) {
      const form = document.getElementById("deleteForm")
      form.action = `/category/${id}/delete`
      form.submit()
    }
    return false
  }
}

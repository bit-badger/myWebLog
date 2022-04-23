const Admin = {
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

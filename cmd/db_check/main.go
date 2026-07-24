package main

import (
	"database/sql"
	"fmt"
	"log"
	"os"

	"lokal-thesis/internal/database"
	"lokal-thesis/internal/runtimepaths"
)

func main() {
	paths, err := runtimepaths.Resolve()
	if err != nil {
		log.Fatal(err)
	}
	databasePath := paths.Database
	if len(os.Args) > 1 {
		databasePath = os.Args[1]
	}

	db, err := database.New(databasePath)
	if err != nil {
		log.Fatal(err)
	}
	defer db.Close()
	fmt.Println("Database:", databasePath)

	rows, err := db.Query(`SELECT id, activity_id, participant_id, answer, is_correct, stars_earned FROM responses ORDER BY id DESC LIMIT 10`)
	if err != nil {
		log.Fatal(err)
	}
	defer rows.Close()

	fmt.Println("Latest Responses:")
	for rows.Next() {
		var id, actID, partID, stars int
		var ans string
		var isCorrect sql.NullBool
		err = rows.Scan(&id, &actID, &partID, &ans, &isCorrect, &stars)
		if err != nil {
			log.Fatal(err)
		}

		correctStr := "NULL"
		if isCorrect.Valid {
			correctStr = fmt.Sprintf("%v", isCorrect.Bool)
		}

		fmt.Printf("ID:%d Act:%d Part:%d Ans:%s Correct:%s Stars:%d\n", id, actID, partID, ans, correctStr, stars)
	}

	fmt.Println("Latest Activities:")
	rowsAct, err := db.Query(`SELECT id, config FROM activities ORDER BY id DESC LIMIT 2`)
	if err == nil {
		for rowsAct.Next() {
			var id int
			var config string
			rowsAct.Scan(&id, &config)
			fmt.Printf("Act ID:%d Config:%s\n", id, config)
		}
		rowsAct.Close()
	}
}

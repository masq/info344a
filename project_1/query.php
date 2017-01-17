<?php
  $name = $_GET['player_name'];
  if (isset($name)) {
    //console_log("[*] Player name is $name");
    $username = "swalden1";
    $password = "Lamepassword98!";
    $table = "nba_stats";

    try {
      $conn = new PDO('mysql:host=info344a.cfmmjmynkitf.us-west-2.rds.amazonaws.com;dbname=info344a', $username, $password);
      $conn->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);

      $query = $conn->prepare("SELECT * FROM $table WHERE name LIKE :name");
      $query->execute(array("name" => "%$name%"));

      $results = $query->fetchAll(PDO::FETCH_ASSOC);

      // output formatted to JSON
      echo json_encode($results);
      header("Content-Type: application/json; charset=UTF-8");
    } catch(PDOException $error) {
      echo json_encode($error->getMessage());
    }
  } else {
    console_log("[*] Player name not set");
  }

  function console_log($message) {
    echo "<script>console.log('$message')</script>";
  }
?>
